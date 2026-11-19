// The whole state of a battle as bytes, and back (not in the web version): every field of every
// object reachable from the world, private ones included, as the code declares them. Nothing is
// listed by hand: a field added to the simulation is saved with the rest. References are kept
// (two orders sharing one fleet's group share one after reading, a point defense round still
// flies at the same torpedo), and collections keep their order, which is how the simulation walks
// them. Replaying a battle from its start would give the same world, but takes minutes for an
// hour of battle; this takes a fraction of a second.
//
// The bytes start with a table of the types written, each with its fields' names and types: a
// save read by a game whose battle state differs in any way (a field added, renamed or retyped)
// is refused as another version's, never read into the wrong fields. Reading never trusts the
// bytes: only the simulation's own types, strings, numbers and the plain collections holding them
// are made; every count is bounded by what is left to read; a broken input throws
// InvalidDataException or EndOfStreamException, nothing else.
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace SandboxShip.Sim;

public static class StateWire
{
    /// <summary>Objects within objects, at most: the battle state goes a dozen deep.</summary>
    const int MaxDepth = 64;
    /// <summary>The longest text read: names and types are short.</summary>
    const int MaxText = 1 << 16;

    enum Shape : byte { Text, Object, Struct, Array, List, Dictionary, Set }

    /// <summary>How a type is written: its shape, its fields in order, its items' types.</summary>
    sealed class Layout(Type type, Shape shape)
    {
        public readonly Type Type = type;
        public readonly Shape Shape = shape;
        public FieldInfo[] Fields = [];
        public Type Item = typeof(object), Key = typeof(object);
        public MethodInfo? Add;
    }

    /// <summary>The bytes of everything reachable from `root`; NotSupportedException for a value of a type it cannot write.</summary>
    public static byte[] Write(object root)
    {
        var writer = new Writer();
        writer.Reference(root, root.GetType());
        return writer.Finish();
    }

    /// <summary>What Write wrote, as a T; InvalidDataException or EndOfStreamException for anything else.</summary>
    public static T Read<T>(byte[] bytes) where T : class
    {
        using var ms = new MemoryStream(bytes, false);
        using var r = new BinaryReader(ms, Encoding.UTF8);
        var reader = new Reader(r, bytes.Length);
        reader.Types();
        var root = reader.Reference(typeof(T)) as T ?? throw new InvalidDataException("Sauvegarde vide.");
        if (ms.Position != ms.Length) throw new InvalidDataException("Sauvegarde abîmée : octets en trop.");
        return root;
    }

    // What a type is, and how it is named in the table.

    static readonly Assembly Own = typeof(World).Assembly;

    /// <summary>The plain collections a state may hold, by the name the table gives them.</summary>
    static readonly Dictionary<string, Type> Generics = new()
    {
        [nameof(List<int>)] = typeof(List<>), [nameof(Dictionary<int, int>)] = typeof(Dictionary<,>), [nameof(HashSet<int>)] = typeof(HashSet<>), [nameof(Nullable<int>)] = typeof(Nullable<>),
        ["ValueTuple`2"] = typeof(ValueTuple<,>), ["ValueTuple`3"] = typeof(ValueTuple<,,>), ["ValueTuple`4"] = typeof(ValueTuple<,,,>),
    };

    static readonly Dictionary<string, Type> Primitives = new[]
    {
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(char), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(string), typeof(object),
    }.ToDictionary(t => t.FullName!);

    /// <summary>
    /// A type's name in the table, the same in every process: "SandboxShip.Sim.Entity",
    /// "List&lt;SandboxShip.Sim.Entity&gt;", "System.Int32[]". Also how a field's declared type is
    /// compared, which may be an interface ("IReadOnlyList&lt;SandboxShip.Sim.Point&gt;").
    /// </summary>
    static string Name(Type t)
    {
        if (t.IsSZArray) return Name(t.GetElementType()!) + "[]";
        if (!t.IsGenericType) return t.FullName ?? t.Name;
        var def = t.GetGenericTypeDefinition();
        var name = (def.Assembly == Own ? def.FullName! : def.Name).Split('`')[0];
        if (name == "ValueTuple") name = def.Name;
        return $"{name}<{string.Join(",", t.GetGenericArguments().Select(Name))}>";
    }

    /// <summary>
    /// Types within a type's name, at most: an array's items, a collection's (the battle state goes
    /// three deep, "Dictionary&lt;Side,List&lt;HistoryPoint&gt;&gt;"). A forged name of thousands would
    /// have the reading recurse until the stack overflows, which no exception handler catches.
    /// </summary>
    public const int MaxNameDepth = 8;

    /// <summary>The type a name of the table stands for, among those a state may hold; InvalidDataException for any other.</summary>
    static Type Resolve(string name, int depth = 0)
    {
        if (depth > MaxNameDepth) throw new InvalidDataException("Sauvegarde abîmée : type trop imbriqué.");
        if (name.EndsWith("[]", StringComparison.Ordinal)) return Resolve(name[..^2], depth + 1).MakeArrayType();
        if (Primitives.TryGetValue(name, out var p)) return p;
        var open = name.IndexOf('<');
        if (open < 0)
        {
            return Plain(name) && Own.GetType(name, false) is { IsGenericTypeDefinition: false, IsPointer: false, IsByRef: false } t && !typeof(Delegate).IsAssignableFrom(t)
                ? t : throw new InvalidDataException($"Type inconnu : {name}");
        }
        if (!name.EndsWith('>')) throw new InvalidDataException($"Type mal formé : {name}");
        var args = Split(name[(open + 1)..^1]).Select(a => Resolve(a, depth + 1)).ToArray();
        var head = name[..open];
        var def = Generics.GetValueOrDefault(head) ?? Generics.GetValueOrDefault($"{head}`{args.Length}")
            ?? (Plain(head) ? Own.GetType($"{head}`{args.Length}", false) : null) ?? throw new InvalidDataException($"Type inconnu : {name}");
        if (!def.IsGenericTypeDefinition || def.GetGenericArguments().Length != args.Length) throw new InvalidDataException($"Type mal formé : {name}");
        try
        {
            return def.MakeGenericType(args);
        }
        catch (ArgumentException)
        {
            throw new InvalidDataException($"Type mal formé : {name}");
        }
    }

    /// <summary>
    /// A name the assembly is asked for as it is: letters, digits, dots, '+' of a nested type, '_'.
    /// Assembly.GetType reads a grammar of its own (brackets, commas, assembly names) that a file
    /// has no business reaching.
    /// </summary>
    static bool Plain(string name) => name.Length is > 0 and <= 256 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '_');

    /// <summary>"A,B<C,D>,E" → "A", "B<C,D>", "E".</summary>
    static IEnumerable<string> Split(string list)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            if (list[i] == '<') depth++;
            else if (list[i] == '>') depth--;
            else if (list[i] == ',' && depth == 0)
            {
                yield return list[start..i];
                start = i + 1;
            }
        }
        yield return list[start..];
    }

    /// <summary>How a type of value is written (primitives, enums and nullables are written inline); NotSupportedException for one that cannot be.</summary>
    static Layout LayoutOf(Type t)
    {
        if (t == typeof(string)) return new Layout(t, Shape.Text);
        if (t.IsSZArray) return new Layout(t, Shape.Array) { Item = t.GetElementType()! };
        for (var b = t; b is not null && b != typeof(object); b = b.BaseType)
        {
            if (!b.IsGenericType) continue;
            var def = b.GetGenericTypeDefinition();
            var args = b.GetGenericArguments();
            if (def == typeof(List<>) && b == t) return new Layout(t, Shape.List) { Item = args[0] };
            if (def == typeof(HashSet<>) && b == t) return new Layout(t, Shape.Set) { Item = args[0], Add = t.GetMethod(nameof(HashSet<int>.Add), [args[0]]) };
            // Dictionaries of the simulation's own (ShipCounts, RankCounts) are dictionaries too, with nothing more to them.
            if (def == typeof(Dictionary<,>) && (b == t || t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Length == 0 && t.Assembly == Own))
                return new Layout(t, Shape.Dictionary) { Key = args[0], Item = args[1] };
        }
        var own = t.Assembly == Own && !t.IsDefined(typeof(CompilerGeneratedAttribute), false)
            || t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("ValueTuple", StringComparison.Ordinal);
        if (!own || t.IsEnum || t.IsPrimitive || t.IsAbstract || t.IsInterface || typeof(Delegate).IsAssignableFrom(t))
            throw new NotSupportedException($"Type non sauvegardable : {Name(t)}");
        var chain = new List<Type>();
        for (var b = t; b is not null && b != typeof(object) && b != typeof(ValueType); b = b.BaseType)
        {
            if (b.Assembly != Own && !b.IsValueType) throw new NotSupportedException($"Type non sauvegardable : {Name(t)} hérite de {Name(b)}");
            chain.Insert(0, b);
        }
        var fields = chain.SelectMany(b => b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .OrderBy(f => f.MetadataToken)).ToArray();
        return new Layout(t, t.IsValueType ? Shape.Struct : Shape.Object) { Fields = fields };
    }

    /// <summary>
    /// A read-only sequence of a type the table cannot name (one a collection expression made,
    /// in whatever assembly) is written as a list of its items: what reads it only reads it.
    /// </summary>
    static Type? AsList(Type runtime, Type declared)
    {
        if (!declared.IsInterface) return null;
        var items = runtime.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (items is null) return null;
        var list = typeof(List<>).MakeGenericType(items.GetGenericArguments()[0]);
        return declared.IsAssignableFrom(list) ? list : null;
    }

    sealed class Writer
    {
        readonly MemoryStream body = new();
        readonly BinaryWriter w;
        readonly Dictionary<object, int> ids = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<Type, int> typeIds = [];
        readonly List<Layout> types = [];
        readonly Dictionary<Type, Layout> layouts = [];
        int depth;

        public Writer() => w = new BinaryWriter(body, Encoding.UTF8, true);

        Layout Of(Type t)
        {
            if (!layouts.TryGetValue(t, out var l))
            {
                layouts[t] = l = LayoutOf(t);
                // A struct's layout goes in the table even though no id is written for it: the reader checks it.
                if (l.Shape == Shape.Struct) Id(l);
            }
            return l;
        }

        int Id(Layout l)
        {
            if (typeIds.TryGetValue(l.Type, out var id)) return id;
            types.Add(l);
            typeIds[l.Type] = id = types.Count;
            return id;
        }

        /// <summary>The table, then the values.</summary>
        public byte[] Finish()
        {
            w.Flush();
            using var all = new MemoryStream();
            using (var head = new BinaryWriter(all, Encoding.UTF8, true))
            {
                head.Write7BitEncodedInt(types.Count);
                foreach (var l in types)
                {
                    head.Write(Name(l.Type));
                    head.Write7BitEncodedInt(l.Fields.Length);
                    foreach (var f in l.Fields)
                    {
                        head.Write(f.Name);
                        head.Write(Name(f.FieldType));
                    }
                }
            }
            body.WriteTo(all);
            return all.ToArray();
        }

        public void Value(object? v, Type declared)
        {
            if (Nullable.GetUnderlyingType(declared) is { } under)
            {
                w.Write(v is not null);
                if (v is not null) Value(v, under);
                return;
            }
            if (declared.IsEnum)
            {
                Integer(v!, Enum.GetUnderlyingType(declared));
                return;
            }
            if (declared.IsPrimitive)
            {
                Integer(v!, declared);
                return;
            }
            if (declared == typeof(string))
            {
                Text((string?)v);
                return;
            }
            if (declared.IsValueType)
            {
                var l = Of(declared);
                foreach (var f in l.Fields) Value(f.GetValue(v), f.FieldType);
                return;
            }
            Reference(v, declared);
        }

        void Integer(object v, Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: w.Write((bool)v); break;
                case TypeCode.Byte: w.Write(Convert.ToByte(v)); break;
                case TypeCode.SByte: w.Write(Convert.ToSByte(v)); break;
                case TypeCode.Int16: w.Write(Convert.ToInt16(v)); break;
                case TypeCode.UInt16: w.Write(Convert.ToUInt16(v)); break;
                case TypeCode.Char: w.Write((ushort)(char)v); break;
                case TypeCode.Int32: w.Write7BitEncodedInt64(Zig(Convert.ToInt32(v))); break;
                case TypeCode.UInt32: w.Write7BitEncodedInt64(Convert.ToUInt32(v)); break;
                case TypeCode.Int64: w.Write7BitEncodedInt64(Zig(Convert.ToInt64(v))); break;
                case TypeCode.UInt64: w.Write((ulong)Convert.ToUInt64(v)); break;
                case TypeCode.Single: w.Write((float)v); break;
                case TypeCode.Double: w.Write((double)v); break;
                default: throw new NotSupportedException($"Type non sauvegardable : {Name(t)}");
            }
        }

        static long Zig(long v) => (v << 1) ^ (v >> 63);

        void Text(string? s)
        {
            if (s is null)
            {
                w.Write7BitEncodedInt(0);
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(s);
            w.Write7BitEncodedInt(bytes.Length + 1);
            w.Write(bytes);
        }

        /// <summary>0 for null; an object already written, by its number (even); a new one, by its type's number (odd), then its content.</summary>
        public void Reference(object? v, Type declared)
        {
            if (v is null)
            {
                w.Write7BitEncodedInt(0);
                return;
            }
            if (ids.TryGetValue(v, out var known))
            {
                w.Write7BitEncodedInt(known << 1);
                return;
            }
            var t = v.GetType();
            Layout l;
            try
            {
                l = Of(t);
            }
            catch (NotSupportedException) when (AsList(t, declared) is { } list)
            {
                l = Of(list);
            }
            // A struct has no identity: one boxed where an object is declared has no place here.
            if (l.Shape == Shape.Struct) throw new NotSupportedException($"Valeur non sauvegardable à cette place : {Name(t)}");
            w.Write7BitEncodedInt(Id(l) << 1 | 1);
            ids[v] = ids.Count + 1;
            if (++depth > MaxDepth) throw new NotSupportedException("Objets imbriqués trop profondément");
            switch (l.Shape)
            {
                case Shape.Text:
                    Text((string)v);
                    break;
                case Shape.Object:
                    foreach (var f in l.Fields) Value(f.GetValue(v), f.FieldType);
                    break;
                case Shape.Array or Shape.List or Shape.Set:
                    if (l.Shape == Shape.Set && !DefaultComparer(v, l.Item)) throw new NotSupportedException($"Comparateur non sauvegardable : {Name(t)}");
                    var items = (IEnumerable)v;
                    var count = 0;
                    foreach (var _ in items) count++;
                    w.Write7BitEncodedInt(count);
                    foreach (var item in items) Value(item, l.Item);
                    break;
                case Shape.Dictionary:
                    var d = (IDictionary)v;
                    if (!DefaultComparer(d, l.Key)) throw new NotSupportedException($"Comparateur non sauvegardable : {Name(t)}");
                    w.Write7BitEncodedInt(d.Count);
                    foreach (DictionaryEntry e in d)
                    {
                        Value(e.Key, l.Key);
                        Value(e.Value, l.Item);
                    }
                    break;
            }
            depth--;
        }

        /// <summary>A dictionary or a set read back is made with the default comparer: one with another would not find the same keys.</summary>
        static bool DefaultComparer(object collection, Type key)
        {
            var comparer = collection.GetType().GetProperty(nameof(HashSet<int>.Comparer))?.GetValue(collection);
            return comparer is null || comparer.Equals(typeof(EqualityComparer<>).MakeGenericType(key).GetProperty("Default")!.GetValue(null));
        }
    }

    sealed class Reader(BinaryReader r, long length)
    {
        readonly List<object> objects = [];
        Layout[] types = [];
        readonly Dictionary<Type, Layout> structs = [];
        readonly Dictionary<Type, (long Mask, HashSet<long>? Values)> enums = [];
        int depth;

        long Left => length - r.BaseStream.Position;

        /// <summary>A count read where each item takes at least a byte: never more than what is left.</summary>
        int Count()
        {
            var n = r.ReadVarint();
            if (n < 0 || n > Left) throw new InvalidDataException("Sauvegarde abîmée : compte impossible.");
            return n;
        }

        /// <summary>The table: each type must be one this game knows, with the very fields this game gives it.</summary>
        public void Types()
        {
            var n = Count();
            types = new Layout[n];
            for (var i = 0; i < n; i++)
            {
                var type = Resolve(r.ReadText(MaxText));
                Layout l;
                try
                {
                    l = LayoutOf(type);
                }
                catch (NotSupportedException e)
                {
                    throw new InvalidDataException(e.Message);
                }
                var fields = Count();
                if (fields != l.Fields.Length) throw Other(type);
                for (var f = 0; f < fields; f++)
                {
                    if (r.ReadText(MaxText) != l.Fields[f].Name || r.ReadText(MaxText) != Name(l.Fields[f].FieldType)) throw Other(type);
                }
                types[i] = l;
                if (l.Shape == Shape.Struct) structs[type] = l;
            }
        }

        static InvalidDataException Other(Type t) => new($"Sauvegarde d'une autre version du jeu ({Name(t)} a changé).");

        public object? Value(Type declared)
        {
            if (Nullable.GetUnderlyingType(declared) is { } under) return r.ReadBoolean() ? Value(under) : null;
            if (declared.IsEnum) return Enum(declared);
            if (declared.IsPrimitive) return Integer(declared);
            if (declared == typeof(string)) return Text();
            if (declared.IsValueType)
            {
                // Every struct written is in the table, checked there.
                if (!structs.TryGetValue(declared, out var l)) throw new InvalidDataException($"Sauvegarde abîmée : {Name(declared)} absent de la table.");
                var box = RuntimeHelpers.GetUninitializedObject(declared);
                foreach (var f in l.Fields) f.SetValue(box, Value(f.FieldType));
                return box;
            }
            return Reference(declared);
        }

        object Integer(Type t) => Type.GetTypeCode(t) switch
        {
            TypeCode.Boolean => r.ReadByte() switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("Sauvegarde abîmée : booléen."),
            },
            TypeCode.Byte => r.ReadByte(),
            TypeCode.SByte => r.ReadSByte(),
            TypeCode.Int16 => r.ReadInt16(),
            TypeCode.UInt16 => r.ReadUInt16(),
            TypeCode.Char => (char)r.ReadUInt16(),
            TypeCode.Int32 => Unzig(r.ReadVarint64()) is var i and >= int.MinValue and <= int.MaxValue ? (int)i : throw new InvalidDataException("Sauvegarde abîmée : nombre."),
            TypeCode.UInt32 => r.ReadVarint64() is var u and >= 0 and <= uint.MaxValue ? (uint)u : throw new InvalidDataException("Sauvegarde abîmée : nombre."),
            TypeCode.Int64 => Unzig(r.ReadVarint64()),
            TypeCode.UInt64 => r.ReadUInt64(),
            TypeCode.Single => r.ReadSingle(),
            TypeCode.Double => r.ReadDouble(),
            _ => throw new InvalidDataException($"Type illisible : {Name(t)}"),
        };

        static long Unzig(long v) => (long)((ulong)v >> 1) ^ -(v & 1);

        /// <summary>A value of the enum it has; for flags, only the bits its values have.</summary>
        object Enum(Type t)
        {
            var value = Convert.ToInt64(Integer(System.Enum.GetUnderlyingType(t)));
            if (!enums.TryGetValue(t, out var known))
            {
                var all = System.Enum.GetValues(t).Cast<object>().Select(Convert.ToInt64).ToList();
                enums[t] = known = t.IsDefined(typeof(FlagsAttribute), false) ? (all.Aggregate(0L, (a, b) => a | b), null) : (0, all.ToHashSet());
            }
            var ok = known.Values is null ? (value & ~known.Mask) == 0 : known.Values.Contains(value);
            return ok ? System.Enum.ToObject(t, value) : throw new InvalidDataException($"{t.Name} inconnu : {value}");
        }

        string? Text()
        {
            var n = r.ReadVarint();
            if (n == 0) return null;
            if (n < 0 || n - 1 > MaxText || n - 1 > Left) throw new InvalidDataException("Sauvegarde abîmée : texte.");
            var bytes = r.ReadBytes(n - 1);
            if (bytes.Length != n - 1) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        public object? Reference(Type declared)
        {
            var tag = r.ReadVarint();
            if (tag == 0) return null;
            if ((tag & 1) == 0)
            {
                var index = (tag >> 1) - 1;
                if ((uint)index >= (uint)objects.Count) throw new InvalidDataException("Sauvegarde abîmée : référence.");
                var known = objects[index];
                return declared.IsInstanceOfType(known) ? known : throw new InvalidDataException("Sauvegarde abîmée : référence d'un autre type.");
            }
            var id = (tag >> 1) - 1;
            if ((uint)id >= (uint)types.Length) throw new InvalidDataException("Sauvegarde abîmée : type.");
            var l = types[id];
            if (l.Shape == Shape.Struct || !declared.IsAssignableFrom(l.Type)) throw new InvalidDataException($"Sauvegarde abîmée : {Name(l.Type)} au lieu de {Name(declared)}.");
            if (++depth > MaxDepth) throw new InvalidDataException("Sauvegarde abîmée : objets trop imbriqués.");
            object o;
            switch (l.Shape)
            {
                case Shape.Text:
                    o = Text() ?? throw new InvalidDataException("Sauvegarde abîmée : texte.");
                    objects.Add(o);
                    break;
                case Shape.Object:
                    o = RuntimeHelpers.GetUninitializedObject(l.Type);
                    objects.Add(o);
                    foreach (var f in l.Fields) f.SetValue(o, Value(f.FieldType));
                    break;
                case Shape.Array:
                    var array = Array.CreateInstance(l.Item, Count());
                    o = array;
                    objects.Add(o);
                    for (var i = 0; i < array.Length; i++) array.SetValue(Value(l.Item), i);
                    break;
                case Shape.List:
                    var n = Count();
                    var list = (IList)Activator.CreateInstance(l.Type, n)!;
                    o = list;
                    objects.Add(o);
                    for (var i = 0; i < n; i++) list.Add(Value(l.Item));
                    break;
                case Shape.Set:
                    var count = Count();
                    o = Activator.CreateInstance(l.Type)!;
                    objects.Add(o);
                    var args = new object?[1];
                    for (var i = 0; i < count; i++)
                    {
                        args[0] = Value(l.Item);
                        l.Add!.Invoke(o, args);
                    }
                    break;
                default:
                    var entries = Count();
                    var d = (IDictionary)Activator.CreateInstance(l.Type)!;
                    o = d;
                    objects.Add(o);
                    for (var i = 0; i < entries; i++)
                    {
                        var key = Value(l.Key) ?? throw new InvalidDataException("Sauvegarde abîmée : clé vide.");
                        if (d.Contains(key)) throw new InvalidDataException("Sauvegarde abîmée : clé en double.");
                        d.Add(key, Value(l.Item));
                    }
                    break;
            }
            depth--;
            return o;
        }
    }
}
