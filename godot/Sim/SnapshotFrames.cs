// The entity records and shots of recent battle snapshots (not in the web version), for deltas:
// a host keeps those it sent a player, the player those it read. A snapshot is written against
// the newest one the player acknowledged (its baseline): an entity whose record did not change
// since is left out, and only the parts of a record that changed go; so is a shot that flies on
// (see Snapshot.cs). Both ends hold the same bytes for the same snapshot, so the player rebuilds
// every record.
namespace SandboxShip.Sim;

public sealed class SnapshotFrames
{
    /// <summary>
    /// Snapshots kept, a second and a half at twenty a second. A baseline older than that is
    /// forgotten: the next snapshot goes whole. A player that has read a baseline still keeps it
    /// for as long as the host does, having read no more snapshots since than the host sent.
    /// </summary>
    public const int Kept = 32;

    /// <summary>Parts of an entity's record (see Snapshot.WriteRecord): who it is, where, its health, the rest.</summary>
    internal const int Segments = 4;

    /// <summary>A shot as a snapshot gives it.</summary>
    internal readonly record struct Shot(int Id, int Side, byte Kind, float X, float Y, float Vx, float Vy)
    {
        /// <summary>
        /// Where it is `dt` seconds later, flying straight: a shot whose speed did not change since
        /// the baseline is left out, and both ends carry it this way, to the same bits.
        /// </summary>
        public Shot After(double dt) => this with { X = (float)(X + Vx * dt), Y = (float)(Y + Vy * dt) };

        public bool Flies(Shot later) => Side == later.Side && Kind == later.Kind && Vx == later.Vx && Vy == later.Vy;
    }

    internal sealed class Frame
    {
        public int Seq;
        /// <summary>The battle's time, and its shots by increasing ids.</summary>
        public double Time;
        public readonly List<Shot> Shots = [];
        public readonly MemoryStream Data = new();
        public readonly BinaryWriter Writer;
        public readonly BinaryReader Reader;
        /// <summary>Entity ids in increasing order, and where each one's segments start in Data, then where its record ends.</summary>
        public readonly List<int> Ids = [];
        public readonly List<int> Starts = [];

        public Frame()
        {
            Writer = new BinaryWriter(Data);
            Reader = new BinaryReader(Data);
        }

        public void Clear(int seq)
        {
            Seq = seq;
            Shots.Clear();
            Data.SetLength(0);
            Ids.Clear();
            Starts.Clear();
        }

        public int Length(int entity, int segment) => Starts[entity * (Segments + 1) + segment + 1] - Starts[entity * (Segments + 1) + segment];

        public ReadOnlySpan<byte> Segment(int entity, int segment)
        {
            var at = Starts[entity * (Segments + 1) + segment];
            return Data.GetBuffer().AsSpan(at, Length(entity, segment));
        }

        /// <summary>The reader, at the start of an entity's segment.</summary>
        public BinaryReader At(int entity, int segment)
        {
            Data.Position = Starts[entity * (Segments + 1) + segment];
            return Reader;
        }

        /// <summary>Index of an entity in this frame, or -1.</summary>
        public int IndexOf(int id) => Ids.BinarySearch(id) is var i && i >= 0 ? i : -1;
    }

    /// <summary>Oldest first.</summary>
    readonly List<Frame> frames = [];
    readonly Stack<Frame> spare = [];
    int next = 1, acked;
    /// <summary>Scratch, kept from one snapshot to the next: entities listed and their ids, what changed in each, ids left out.</summary>
    internal readonly List<(Entity E, bool Remembered)> Listed = [];
    internal readonly HashSet<int> Shown = [];
    internal readonly List<byte> Masks = [];
    internal readonly List<int> Removed = [];
    internal readonly List<byte> ShotMasks = [];
    internal readonly List<int> ShotRemoved = [];

    /// <summary>
    /// A player: the snapshot whose parts are coming (Snapshot.ReadPart), its number, the index
    /// of the part expected next, and whether one was missed (it is then not kept).
    /// </summary>
    internal Frame? Building;
    internal int BuildingSeq, Expected;
    internal bool Broken;

    /// <summary>The newest snapshot read or written.</summary>
    public int Latest => frames.Count > 0 ? frames[^1].Seq : 0;

    /// <summary>Host: the player read this snapshot; the next ones are written against it while it is kept. Any other number is ignored.</summary>
    public void Ack(int seq)
    {
        if (Find(seq) is not null) acked = seq;
    }

    /// <summary>Host: the frame of the next snapshot, and its baseline (null: it goes whole).</summary>
    internal (Frame Frame, Frame? Baseline) Next()
    {
        var baseline = Find(acked);
        return (Take(next++), baseline);
    }

    internal Frame? Find(int seq)
    {
        if (seq <= 0) return null;
        foreach (var f in frames) if (f.Seq == seq) return f;
        return null;
    }

    /// <summary>An empty frame for snapshot `seq`, kept once Keep is called.</summary>
    internal Frame Take(int seq)
    {
        var f = spare.Count > 0 ? spare.Pop() : new Frame();
        f.Clear(seq);
        return f;
    }

    /// <summary>Keeps a frame written or read whole, the newest; frames past Kept go back to the spares.</summary>
    internal void Keep(Frame f)
    {
        frames.Add(f);
        while (frames.Count > Kept)
        {
            spare.Push(frames[0]);
            frames.RemoveAt(0);
        }
    }

    /// <summary>A frame that will not be kept (a snapshot that did not read to its end).</summary>
    internal void Discard(Frame f) => spare.Push(f);
}
