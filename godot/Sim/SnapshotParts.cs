// Battle snapshots in parts (not in the web version), each small enough for one packet. Sent
// whole, a big battle's delta went as a dozen ENet fragments, and one of them lost was the whole
// snapshot lost. In parts, each one reads on its own: a lost part leaves its entities a snapshot
// behind, the others move on. The snapshot is still written against the newest one the player
// acknowledged, and the player acknowledges it only once every part of it came: the next one is
// then written against it, and mends whatever a lost part left behind.
//
// Every part starts the same: u32 magic, u16 version, n viewer, n snapshot number, n baseline,
// u8 kind, n index, n count, f64 time. Then, by kind:
//   head      what Write puts before the entities (world, mode, sides, fields), then after the
//             shots (beams, zones, strikes, events); part 0, always
//   entities  n first id, n end (0: none), then as Write's entities but for the ids from the
//             first to before the end: n count, the ids left out, those new or changed
//   shots     the same for shots
// Parts go in order, entities by increasing ids then shots; together they cover every id.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static partial class Snapshot
{
    /// <summary>
    /// Bytes a part aims at, before compression: with ENet's and the message's headers, under
    /// the 1400 bytes of a packet. A single record bigger than it goes alone, in a part of its own.
    /// </summary>
    public const int PartBudget = 1100;

    const byte HeadPart = 0, EntityPart = 1, ShotPart = 2;

    /// <summary>
    /// What reading a part did: whether it read (false: written against a baseline this player
    /// does not have, nothing changed), the snapshot it belongs to, the one it completed (0 until
    /// its last part; and never when a part of it was lost), its kind and the ids it covers.
    /// </summary>
    public readonly record struct PartRead(bool Read, int Seq, int Completed, int Kind, int First, int End)
    {
        public bool Entities => Kind == EntityPart;
        public bool Shots => Kind == ShotPart;
        /// <summary>Whether an id lies in what the part covers (End 0: no end).</summary>
        public bool Covers(int id) => id >= First && (End == 0 || id < End);
    }

    /// <summary>
    /// Writes what `viewer`'s team may see of the battle as Write does, but in parts of about
    /// `budget` bytes, to send one a packet: the head first, then the entities, then the shots.
    /// The snapshot is the next of `frames`, written against the newest acknowledged.
    /// </summary>
    public static List<byte[]> WriteParts(World world, Side viewer, IReadOnlyList<SimEvent>? events, BinaryWriter? vital, SnapshotFrames frames,
        int budget = PartBudget)
    {
        var (frame, baseline) = frames.Next();
        var shown = WriteEntities(world, viewer, null, frame, baseline, frames);
        var sight = new Sight(world, viewer);
        FillShots(world, viewer, sight, frame, baseline, frames.ShotMasks, frames.ShotRemoved);
        frames.Keep(frame);
        var entityMasks = frames.Masks;
        var entityRemoved = frames.Removed;
        Diff(frame, baseline, entityMasks, entityRemoved);
        var bodies = new List<(byte Kind, byte[] Body)>();
        Ranges(frame.Ids.Count, entityRemoved, i => frame.Ids[i], i => entityMasks[i] == 0 ? 0 : 4 + Changed(frame, i, entityMasks[i]), budget,
            (first, end, from, to, gone) => bodies.Add((EntityPart, EntityRange(frame, entityMasks, entityRemoved, first, end, from, to, gone))));
        Ranges(frame.Shots.Count, frames.ShotRemoved, i => frame.Shots[i].Id, i => frames.ShotMasks[i] == 0 ? 0 : 24, budget,
            (first, end, from, to, gone) => bodies.Add((ShotPart, ShotRange(frame, frames.ShotMasks, frames.ShotRemoved, first, end, from, to, gone))));
        var parts = new List<byte[]>(bodies.Count + 1);
        var count = bodies.Count + 1;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            PartHeader(w, world, viewer, frame, baseline, HeadPart, 0, count);
            WriteWorld(world, viewer, w);
            WriteTail(world, viewer, shown, sight, w, events, vital);
            parts.Add(ms.ToArray());
        }
        for (var i = 0; i < bodies.Count; i++)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            PartHeader(w, world, viewer, frame, baseline, bodies[i].Kind, i + 1, count);
            w.Write(bodies[i].Body);
            parts.Add(ms.ToArray());
        }
        return parts;
    }

    static void PartHeader(BinaryWriter w, World world, Side viewer, SnapshotFrames.Frame frame, SnapshotFrames.Frame? baseline, byte kind, int index, int count)
    {
        w.Write(Magic);
        w.Write(Version);
        w.Write7BitEncodedInt(viewer.Id);
        w.Write7BitEncodedInt(frame.Seq);
        w.Write7BitEncodedInt(baseline?.Seq ?? 0);
        w.Write(kind);
        w.Write7BitEncodedInt(index);
        w.Write7BitEncodedInt(count);
        w.Write(world.Time);
    }

    /// <summary>Bytes the changed segments of an entity weigh.</summary>
    static int Changed(SnapshotFrames.Frame frame, int i, int mask)
    {
        var n = 0;
        for (var k = 0; k < Segments; k++) if ((mask & 1 << k) != 0) n += frame.Length(i, k);
        return n;
    }

    /// <summary>
    /// Cuts the ids of a frame (`count` of them, `id` of each, `cost` in bytes) and those left
    /// out since the baseline (`removed`, by increasing ids, a few bytes each) into ranges of
    /// about `budget` bytes, in order, each given to `emit` as (first id, end id or 0, frame
    /// indexes from, to, then the removed ones in it). At least one range, covering every id.
    /// </summary>
    static void Ranges(int count, List<int> removed, Func<int, int> id, Func<int, int> cost, int budget,
        Action<int, int, int, int, List<int>> emit)
    {
        // Ids start at 1: the first range too, its gaps counted from 0 as in a whole snapshot.
        int i = 0, j = 0, first = 1, from = 0, used = 0;
        var gone = new List<int>();
        while (i < count || j < removed.Count)
        {
            var out_ = j < removed.Count && (i >= count || removed[j] < id(i));
            var at = out_ ? removed[j] : id(i);
            var c = out_ ? 3 : cost(i);
            if (used > 0 && used + c > budget)
            {
                emit(first, at, from, i, gone);
                (first, from, used, gone) = (at, i, 0, []);
            }
            used += c;
            if (out_) gone.Add(removed[j++]);
            else i++;
        }
        emit(first, 0, from, i, gone);
    }

    static byte[] EntityRange(SnapshotFrames.Frame frame, List<byte> masks, List<int> removed, int first, int end, int from, int to, List<int> gone)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write7BitEncodedInt(first);
        w.Write7BitEncodedInt(end);
        w.Write7BitEncodedInt(to - from);
        WriteIdsFrom(w, gone, first - 1);
        var changed = 0;
        for (var i = from; i < to; i++) if (masks[i] != 0) changed++;
        w.Write7BitEncodedInt(changed);
        var last = first - 1;
        for (var i = from; i < to; i++)
        {
            if (masks[i] == 0) continue;
            w.Write7BitEncodedInt(frame.Ids[i] - last);
            last = frame.Ids[i];
            w.Write(masks[i]);
            for (var k = 0; k < Segments; k++) if ((masks[i] & 1 << k) != 0) w.Write(frame.Segment(i, k));
        }
        return ms.ToArray();
    }

    static byte[] ShotRange(SnapshotFrames.Frame frame, List<byte> masks, List<int> removed, int first, int end, int from, int to, List<int> gone)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write7BitEncodedInt(first);
        w.Write7BitEncodedInt(end);
        w.Write7BitEncodedInt(to - from);
        WriteIdsFrom(w, gone, first - 1);
        var changed = 0;
        for (var i = from; i < to; i++) if (masks[i] != 0) changed++;
        w.Write7BitEncodedInt(changed);
        var last = first - 1;
        for (var i = from; i < to; i++)
        {
            if (masks[i] == 0) continue;
            var s = frame.Shots[i];
            w.Write7BitEncodedInt(s.Id - last);
            last = s.Id;
            w.Write7BitEncodedInt(s.Side);
            w.Write(s.Kind);
            w.Write(s.X);
            w.Write(s.Y);
            w.Write(s.Vx);
            w.Write(s.Vy);
        }
        return ms.ToArray();
    }

    /// <summary>Ids by increasing order, each as the gap from the one before, the first from `last`.</summary>
    static void WriteIdsFrom(BinaryWriter w, List<int> ids, int last)
    {
        w.Write7BitEncodedInt(ids.Count);
        foreach (var id in ids)
        {
            w.Write7BitEncodedInt(id - last);
            last = id;
        }
    }

    /// <summary>
    /// Reads one part into `replica`, as Read does a whole snapshot, for what the part covers. The
    /// parts of a snapshot are put together in `frames` as they come: one missing (lost, or
    /// unreadable) and the snapshot is not kept, so not acknowledged; the parts read still count
    /// on the replica. A part against a baseline this player does not have changes nothing.
    /// </summary>
    public static PartRead ReadPart(BinaryReader r, World replica, SnapshotFrames frames)
    {
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Pas un instantané de bataille");
        var version = r.ReadUInt16();
        if (version != Version) throw new InvalidDataException($"Instantané en version {version}, version {Version} attendue");
        var viewer = new Side(r.ReadVarint());
        var seq = r.ReadVarint();
        var from = r.ReadVarint();
        var kind = r.ReadByte();
        var index = r.ReadVarint();
        var count = r.ReadVarint();
        var time = r.ReadDouble();
        if (seq <= 0 || count < 1 || index >= count || kind > ShotPart) throw new InvalidDataException("Partie d'instantané mal formée");
        var baseline = frames.Find(from);
        if (from != 0 && baseline is null) return new(false, seq, 0, kind, 0, 0);
        if (frames.Building is not { } frame || frames.BuildingSeq != seq)
        {
            if (frames.Building is { } old) frames.Discard(old);
            frame = frames.Building = frames.Take(seq);
            (frames.BuildingSeq, frames.Expected, frames.Broken) = (seq, 0, false);
        }
        if (index != frames.Expected) frames.Broken = true;
        frames.Expected = index + 1;
        replica.Time = time;
        frame.Time = time;
        int first = 0, end = 0;
        try
        {
            switch (kind)
            {
                case HeadPart:
                    ReadWorld(r, replica, viewer);
                    ReadTail(r, replica);
                    break;
                case EntityPart:
                    (first, end) = (Count(r), Count(r));
                    ReadEntityRange(r, replica, viewer, frame, baseline, frames, first, end);
                    break;
                default:
                    (first, end) = (Count(r), Count(r));
                    ReadShotRange(r, replica, frame, baseline, frames, first, end, time);
                    break;
            }
        }
        catch
        {
            frames.Broken = true;
            throw;
        }
        var completed = 0;
        if (index == count - 1)
        {
            frames.Building = null;
            if (frames.Broken) frames.Discard(frame);
            else
            {
                frames.Keep(frame);
                completed = seq;
            }
        }
        return new(true, seq, completed, kind, first, end);
    }

    /// <summary>The entities from `first` to before `end` (0: no end), as ReadEntities reads them all.</summary>
    static void ReadEntityRange(BinaryReader r, World replica, Side viewer, SnapshotFrames.Frame frame, SnapshotFrames.Frame? baseline,
        SnapshotFrames frames, int first, int end)
    {
        bool In(int id) => id >= first && (end == 0 || id < end);
        if (frame.Ids.Count > 0 && frame.Ids[^1] >= first) throw new InvalidDataException("Parties d'instantané dans le désordre");
        var count = Count(r);
        var removed = ReadIdsFrom(r, frames.Removed, first - 1);
        var changes = Count(r);
        var visible = RangeSight(replica.Visible, viewer, In);
        var seen = RangeSight(replica.Seen, viewer, In);
        var old = baseline?.Ids ?? [];
        var b = LowerBound(old, first);
        var stop = end == 0 ? old.Count : LowerBound(old, end);
        var before = frame.Ids.Count;
        var change = changes > 0 ? NextId(r, first - 1) : int.MaxValue;
        var gone = 0;
        while (b < stop || change != int.MaxValue)
        {
            var id = b < stop ? old[b] : int.MaxValue;
            if (id < change)
            {
                if (gone < removed.Count && removed[gone] == id) gone++;
                else ReadRecord(r, replica, viewer, frame, id, 0, baseline, b, visible, seen);
                b++;
                continue;
            }
            if (!In(change)) throw new InvalidDataException($"Entité {change} hors de sa partie");
            ReadRecord(r, replica, viewer, frame, change, r.ReadByte(), baseline, id == change ? b++ : -1, visible, seen);
            change = --changes > 0 ? NextId(r, change) : int.MaxValue;
        }
        if (gone != removed.Count || frame.Ids.Count - before != count) throw new InvalidDataException("Entités mal formées");
        // What the part covers and no longer lists is gone from the replica.
        List<int>? lost = null;
        var listed = frame.Ids.GetRange(before, count);
        foreach (var e in replica.Entities) if (In(e.Id) && listed.BinarySearch(e.Id) < 0) (lost ??= []).Add(e.Id);
        foreach (var id in lost ?? []) replica.Entities.Remove(id);
        Worlds.AttachHardpoints(replica);
    }

    /// <summary>The shots from `first` to before `end` (0: no end); the replica's others stay as they are.</summary>
    static void ReadShotRange(BinaryReader r, World replica, SnapshotFrames.Frame frame, SnapshotFrames.Frame? baseline, SnapshotFrames frames,
        int first, int end, double time)
    {
        bool In(int id) => id >= first && (end == 0 || id < end);
        var shots = frame.Shots;
        if (shots.Count > 0 && shots[^1].Id >= first) throw new InvalidDataException("Parties d'instantané dans le désordre");
        var count = Count(r);
        var removed = ReadIdsFrom(r, frames.Removed, first - 1);
        var changes = Count(r);
        var old = baseline?.Shots ?? [];
        var dt = time - (baseline?.Time ?? 0);
        var b = 0;
        while (b < old.Count && old[b].Id < first) b++;
        var before = shots.Count;
        var change = changes > 0 ? NextId(r, first - 1) : int.MaxValue;
        var gone = 0;
        while (b < old.Count && In(old[b].Id) || change != int.MaxValue)
        {
            var id = b < old.Count && In(old[b].Id) ? old[b].Id : int.MaxValue;
            if (id < change)
            {
                if (gone < removed.Count && removed[gone] == id) gone++;
                else shots.Add(old[b].After(dt));
                b++;
                continue;
            }
            if (!In(change)) throw new InvalidDataException($"Tir {change} hors de sa partie");
            if (id == change) b++;
            shots.Add(new(change, r.ReadVarint(), r.ReadByte(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            change = --changes > 0 ? NextId(r, change) : int.MaxValue;
        }
        if (gone != removed.Count || shots.Count - before != count) throw new InvalidDataException("Tirs mal formés");
        // The replica's shots: those before the range and after it as they were, the range's as read,
        // those still flying keeping their object, for the view that follows them.
        var was = replica.Projectiles;
        var list = new List<Projectile>(was.Count + count);
        var j = 0;
        while (j < was.Count && was[j].Id < first) list.Add(was[j++]);
        for (var i = before; i < shots.Count; i++)
        {
            var s = shots[i];
            while (j < was.Count && In(was[j].Id) && was[j].Id < s.Id) j++;
            var p = j < was.Count && was[j].Id == s.Id ? was[j] : new Projectile { Id = s.Id };
            (p.Side, p.Kind, p.X, p.Y, p.Vx, p.Vy) = (new Side(s.Side), (WeaponKind)s.Kind, s.X, s.Y, s.Vx, s.Vy);
            list.Add(p);
        }
        while (j < was.Count && In(was[j].Id)) j++;
        while (j < was.Count) list.Add(was[j++]);
        replica.Projectiles = list;
    }

    /// <summary>Ids written by WriteIdsFrom, into a list emptied first.</summary>
    static List<int> ReadIdsFrom(BinaryReader r, List<int> into, int last)
    {
        into.Clear();
        for (var n = Count(r); n > 0; n--) into.Add(last = NextId(r, last));
        return into;
    }

    /// <summary>The replica's set of what its viewer sees (or saw), its ids in the range taken out for the part to put back.</summary>
    static HashSet<int> RangeSight(Dictionary<Side, HashSet<int>> sets, Side viewer, Func<int, bool> inRange)
    {
        if (!sets.TryGetValue(viewer, out var set))
        {
            sets.Clear();
            sets[viewer] = set = [];
        }
        else if (sets.Count > 1)
        {
            foreach (var other in sets.Keys.Where(k => k != viewer).ToList()) sets.Remove(other);
        }
        set.RemoveWhere(id => inRange(id));
        return set;
    }

    /// <summary>The index of the first id at least `id`, in increasing ids.</summary>
    static int LowerBound(List<int> ids, int id)
    {
        var i = ids.BinarySearch(id);
        return i >= 0 ? i : ~i;
    }
}
