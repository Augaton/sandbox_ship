// Reading what the network or a file brings (not in the web version), never trusting it: every
// reader refuses broken bytes with InvalidDataException or EndOfStreamException, which their
// callers catch. BinaryReader's own 7-bit numbers throw FormatException on five bytes that never
// end one: a single packet of that kind took a server down. A number that stands for a value of
// the game (a race, a class, a side…) is checked as it is read: one that names nothing would get
// through the reading and bring the screen down later, where a dictionary is asked for it.
using System.Runtime.CompilerServices;

namespace SandboxShip.Sim;

public static class Wire
{
    /// <summary>A value of T from one byte; a byte T has no value for is refused.</summary>
    public static T ReadEnum<T>(this BinaryReader r) where T : struct, Enum => Known<T>(r.ReadByte());

    /// <summary>A value of T from one byte, byte.MaxValue standing for none.</summary>
    public static T? ReadEnumOrNone<T>(this BinaryReader r) where T : struct, Enum => r.ReadByte() is var b && b != byte.MaxValue ? Known<T>(b) : null;

    /// <summary>The value of T numbered `value`, or InvalidDataException when T has none.</summary>
    public static T Known<T>(int value) where T : struct, Enum =>
        Defined<T>.Has(value) ? Unsafe.As<int, T>(ref value) : throw new InvalidDataException($"{typeof(T).Name} inconnu : {value}");

    /// <summary>
    /// A side, as the game numbers them: the races' own 0 to 3 (the Errants 3), then the seats
    /// beyond the third from 4, up to Lobby.MaxSeats.
    /// </summary>
    public static Side ReadSide(this BinaryReader r)
    {
        var id = r.ReadVarint();
        return id is >= 0 and <= Lobby.MaxSeats ? new Side(id) : throw new InvalidDataException($"Camp inconnu : {id}");
    }

    /// <summary>Which numbers T has values for, worked out once: no value is boxed to ask.</summary>
    static class Defined<T> where T : struct, Enum
    {
        static readonly bool[] has = Build();

        public static bool Has(int value) => (uint)value < (uint)has.Length && has[value];

        static bool[] Build()
        {
            // Every enum of the game counts in int from 0.
            if (Enum.GetUnderlyingType(typeof(T)) != typeof(int)) throw new NotSupportedException($"{typeof(T).Name} ne compte pas en int");
            var values = Enum.GetValues<T>().Select(v => Unsafe.As<T, int>(ref v)).Where(v => v >= 0).ToArray();
            var has = new bool[values.Length == 0 ? 0 : values.Max() + 1];
            foreach (var v in values) has[v] = true;
            return has;
        }
    }

    /// <summary>A 7-bit encoded number, as BinaryReader.Read7BitEncodedInt reads it; a malformed one throws InvalidDataException.</summary>
    public static int ReadVarint(this BinaryReader r)
    {
        uint result = 0;
        for (var shift = 0; shift < 28; shift += 7)
        {
            var b = r.ReadByte();
            result |= (b & 0x7Fu) << shift;
            if (b <= 0x7F) return (int)result;
        }
        // The fifth byte carries the last four bits, and nothing more.
        var last = r.ReadByte();
        if (last > 0b1111) throw new InvalidDataException("Nombre mal formé");
        return (int)(result | (uint)last << 28);
    }

    /// <summary>
    /// A text as BinaryWriter.Write(string) writes it (its length in bytes, 7-bit encoded, then
    /// UTF-8), at most `max` bytes: a longer or cut one is refused, nothing is allocated for a forged length.
    /// </summary>
    public static string ReadText(this BinaryReader r, int max = 4096)
    {
        var n = r.ReadVarint();
        if (n < 0 || n > max) throw new InvalidDataException($"Texte de {n} octets");
        var bytes = r.ReadBytes(n);
        if (bytes.Length != n) throw new EndOfStreamException();
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>A 7-bit encoded long, as BinaryReader.Read7BitEncodedInt64 reads it; a malformed one throws InvalidDataException.</summary>
    public static long ReadVarint64(this BinaryReader r)
    {
        ulong result = 0;
        for (var shift = 0; shift < 63; shift += 7)
        {
            var b = r.ReadByte();
            result |= (b & 0x7Ful) << shift;
            if (b <= 0x7F) return (long)result;
        }
        // The tenth byte carries the last bit, and nothing more.
        var last = r.ReadByte();
        if (last > 0b1) throw new InvalidDataException("Nombre mal formé");
        return (long)(result | (ulong)last << 63);
    }
}
