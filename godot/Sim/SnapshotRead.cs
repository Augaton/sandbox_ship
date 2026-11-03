// Reading a battle snapshot into a replica (see Snapshot.cs for the layout): entities are
// created, updated and removed by id, the rest replaced. Nothing of the simulation runs: the
// replica holds what the host said, and only what the viewer's team may see. Every entity's
// record is rebuilt, from the snapshot where it changed and from the baseline elsewhere, and
// read whole into the replica: it holds the snapshot, whatever came in between.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static partial class Snapshot
{
    /// <summary>Weapons on a replica stand in by number: the view only counts them.</summary>
    static readonly WeaponDef Unknown = new(WeaponKind.Bullet, 0, 0, 0, 0);

    /// <summary>
    /// Brings `replica` to the snapshot: the battle as its viewer's team sees it, with the events
    /// since the previous snapshot added to its Events; with `frames`, those this player read,
    /// the snapshot is kept among them once read to its end, a baseline for the next ones. False,
    /// and nothing changes, when its baseline is not one of them (a snapshot written against one
    /// that was lost, or forgotten): the player waits for the next. A snapshot of another
    /// version is refused before anything changes. A snapshot cut short leaves the replica half
    /// updated and is not kept; the next one mends it. The stream must be seekable (a MemoryStream).
    /// </summary>
    public static bool Read(BinaryReader r, World replica, SnapshotFrames? frames = null)
    {
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Pas un instantané de bataille");
        var version = r.ReadUInt16();
        if (version != Version) throw new InvalidDataException($"Instantané en version {version}, version {Version} attendue");
        var viewer = r.ReadSide();
        var seq = r.ReadVarint();
        var from = r.ReadVarint();
        var baseline = frames?.Find(from);
        if (from != 0 && baseline is null) return false;
        var frame = frames?.Take(seq) ?? new SnapshotFrames.Frame();
        var whole = false;
        try
        {
            ReadAll(r, replica, viewer, frame, baseline, frames);
            whole = true;
        }
        finally
        {
            if (whole && seq > 0) frames?.Keep(frame);
            else frames?.Discard(frame);
        }
        return true;
    }

    static void ReadAll(BinaryReader r, World replica, Side viewer, SnapshotFrames.Frame frame, SnapshotFrames.Frame? baseline, SnapshotFrames? frames)
    {
        replica.Time = r.ReadDouble();
        ReadWorld(r, replica, viewer);
        ReadEntities(r, replica, viewer, frame, baseline, frames);
        ReadShots(r, replica, frame, baseline, frames);
        ReadTail(r, replica);
    }

    /// <summary>The world, after its time (see WriteWorld): its size and flags, the mode's state, the sides, the fields.</summary>
    static void ReadWorld(BinaryReader r, World replica, Side viewer)
    {
        replica.Width = r.ReadDouble();
        replica.Height = r.ReadDouble();
        var flags = r.ReadByte();
        replica.Fog = (flags & 1) != 0;
        replica.SandboxTools = (flags & 2) != 0;
        replica.Winner = (flags & 4) != 0 ? r.ReadSide() : null;
        replica.LastDamage = r.ReadDouble();
        ReadMode(r, replica.Mode);
        ReadSides(r, replica, viewer);
        ReadFields(r, replica);
        ReadTerrain(r, replica);
    }

    /// <summary>After the shots (see WriteTail): the beams, the zones, the strikes, the events.</summary>
    static void ReadTail(BinaryReader r, World replica)
    {
        var beams = new List<BeamLine>();
        for (var n = Count(r); n > 0; n--) beams.Add(new BeamLine(r.ReadVarint(), r.ReadVarint(), r.ReadSide()));
        replica.Beams = beams;
        var zones = new List<Zone>();
        for (var n = Count(r); n > 0; n--)
        {
            zones.Add(new Zone(r.ReadEnum<ZoneKind>(), r.ReadSide(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
        }
        replica.Zones = zones;
        var strikes = new List<Strike>();
        for (var n = Count(r); n > 0; n--) strikes.Add(ReadStrike(r));
        replica.Strikes = strikes;
        for (var n = Count(r); n > 0; n--) replica.Events.Add(ReadEvent(r));
    }

    /// <summary>Events that came on the reliable channel (the `vital` stream of Write), added to the replica's Events.</summary>
    public static void ReadEvents(BinaryReader r, World replica)
    {
        for (var n = Count(r); n > 0; n--) replica.Events.Add(ReadEvent(r));
    }

    /// <summary>A count, checked: a broken snapshot must not ask for a huge list.</summary>
    static int Count(BinaryReader r)
    {
        var n = r.ReadVarint();
        return n is >= 0 and <= 1 << 20 ? n : throw new InvalidDataException($"Liste de {n} éléments");
    }

    static int? Optional(BinaryReader r) => r.ReadVarint() is var id && id >= 0 ? id : null;

    /// <summary>The battle mode's state (see ModeBytes), into the replica's own, beacons in place; it must take the whole length announced.</summary>
    static void ReadMode(BinaryReader r, ModeState mode)
    {
        var length = Count(r);
        var bytes = r.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        using var m = new BinaryReader(new MemoryStream(bytes));
        mode.Kind = m.ReadEnum<BattleMode>();
        Side? SideOrNone() => m.ReadVarint() is var id && id > 0 ? id - 1 <= Lobby.MaxSeats ? new Side(id - 1) : throw new InvalidDataException($"Camp inconnu : {id - 1}") : null;
        switch (mode.Kind)
        {
            case BattleMode.Standard: break;
            case BattleMode.Survival:
                mode.Wave = m.ReadVarint();
                mode.Cleared = m.ReadVarint();
                mode.NextWave = m.ReadDouble();
                mode.Held = m.ReadDouble();
                mode.Tallies.Clear();
                for (var t = Count(m); t > 0; t--)
                {
                    mode.Tallies[m.ReadSide()] = new SurvivalTally
                    {
                        Score = m.ReadVarint(), Downed = m.ReadVarint(), Bosses = m.ReadVarint(),
                    };
                }
                break;
            case BattleMode.Zones:
                mode.ZoneTarget = m.ReadVarint();
                var n = Count(m);
                if (mode.Beacons.Count != n) mode.Beacons = [.. Enumerable.Range(0, n).Select(_ => new Beacon(0, 0))];
                foreach (var b in mode.Beacons)
                {
                    b.X = m.ReadSingle();
                    b.Y = m.ReadSingle();
                    b.Holder = SideOrNone();
                    b.Claim = SideOrNone();
                    b.Progress = m.ReadSingle();
                    b.Contested = m.ReadBoolean();
                }
                mode.Scores.Clear();
                for (var i = Count(m); i > 0; i--) mode.Scores[m.ReadSide()] = m.ReadDouble();
                break;
            case BattleMode.Assault:
                mode.Deadline = m.ReadDouble();
                break;
            case BattleMode.Regicide:
                mode.Flagships.Clear();
                for (var i = Count(m); i > 0; i--) mode.Flagships[m.ReadSide()] = m.ReadVarint();
                break;
            default: throw new InvalidDataException($"Mode de bataille inconnu : {mode.Kind}");
        }
        if (m.BaseStream.Position != length) throw new InvalidDataException("État du mode mal formé");
    }

    static void ReadSides(BinaryReader r, World replica, Side viewer)
    {
        var listed = new HashSet<Side>();
        for (var n = Count(r); n > 0; n--)
        {
            var side = r.ReadSide();
            listed.Add(side);
            if (!replica.Factions.TryGetValue(side, out var f)) replica.Factions[side] = f = new FactionState { Id = side };
            f.Race = r.ReadEnum<Faction>();
            f.Team = r.ReadVarint();
            f.Color = r.ReadUInt32();
            var flags = r.ReadByte();
            f.HadStation = (flags & 1) != 0;
            f.StationId = (flags & 2) != 0 ? r.ReadVarint() : null;
            f.Stats.Built = r.ReadVarint();
            f.Stats.Lost = r.ReadVarint();
            f.Stats.Kills = r.ReadVarint();
            f.Stats.Damage = r.ReadSingle();
            f.Stats.Earned = r.ReadSingle();
            if ((flags & 4) != 0) ReadEconomy(r, f);
        }
        foreach (var gone in replica.Factions.Keys.Where(s => !listed.Contains(s)).ToList()) replica.Factions.Remove(gone);
    }

    static void ReadEconomy(BinaryReader r, FactionState f)
    {
        f.Credits = r.ReadDouble();
        f.Bounties = r.ReadDouble();
        f.AutoAbilities = r.ReadBoolean();
        f.RallyX = r.ReadSingle();
        f.RallyY = r.ReadSingle();
        f.Queue = ReadQueue(r);
        f.BuildQueue = ReadQueue(r);
        var techs = Count(r);
        for (var i = 0; i < techs; i++)
        {
            var level = r.ReadVarint();
            if (i < Techs.All.Count) f.Research[Techs.All[i]] = level;
        }
        f.Researching = r.ReadBoolean() ? ReadJob(r) : null;
        f.ResearchQueue = [];
        for (var n = Count(r); n > 0; n--) f.ResearchQueue.Add(ReadJob(r));
        f.Repeat = [];
        for (var n = Count(r); n > 0; n--) f.Repeat.Add(r.ReadEnum<ShipClass>());
        f.HeroCooldowns.Clear();
        for (var n = Count(r); n > 0; n--) f.HeroCooldowns[ReadHero(r)] = r.ReadSingle();
    }

    static List<QueueItem> ReadQueue(BinaryReader r)
    {
        var queue = new List<QueueItem>();
        for (var n = Count(r); n > 0; n--)
        {
            queue.Add(new QueueItem
            {
                Kind = r.ReadEnum<QueueKind>(), Cls = r.ReadEnum<ShipClass>(), TargetId = r.ReadVarint(), Hero = ReadHero(r),
                Remaining = r.ReadSingle(), Total = r.ReadSingle(), Cost = r.ReadSingle(),
            });
        }
        return queue;
    }

    static ResearchJob ReadJob(BinaryReader r)
    {
        var tech = r.ReadEnum<TechId>();
        var remaining = r.ReadSingle();
        return new ResearchJob { Tech = tech, Remaining = remaining, Total = r.ReadSingle(), Cost = r.ReadSingle() };
    }

    /// <summary>A hero: one of the game's, or none (the default value, on an order that is no hero).</summary>
    static HeroId ReadHero(BinaryReader r)
    {
        var h = new HeroId(r.ReadEnum<Faction>(), r.ReadEnum<HeroRole>());
        return h == default || Heroes.Defs.ContainsKey(h) ? h : throw new InvalidDataException($"Héros inconnu : {h}");
    }

    /// <summary>Fields stay where they are for the whole battle: they are kept, and only their mines change.</summary>
    static void ReadFields(BinaryReader r, World replica)
    {
        var fields = new List<AsteroidField>();
        for (var n = Count(r); n > 0; n--)
        {
            fields.Add(new AsteroidField(r.ReadVarint(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) { MineId = Optional(r) });
        }
        if (!fields.Select(f => f.Id).SequenceEqual(replica.Fields.Select(f => f.Id)))
        {
            replica.Fields.Clear();
            replica.Fields.AddRange(fields);
            return;
        }
        for (var i = 0; i < fields.Count; i++) replica.Fields[i].MineId = fields[i].MineId;
    }

    /// <summary>
    /// The terrain (see WriteTerrain): its pieces when they come, then the mines left in each
    /// minefield, which only apply to the pieces as the replica has them.
    /// </summary>
    static void ReadTerrain(BinaryReader r, World replica)
    {
        if (r.ReadBoolean())
        {
            var n = Count(r);
            if (n > MaxTerrain) throw new InvalidDataException($"Terrain de {n} pièces");
            var pieces = new List<TerrainPatch>(n);
            for (var i = 0; i < n; i++)
            {
                var id = r.ReadVarint();
                var kind = r.ReadByte();
                if (!Enum.IsDefined((TerrainKind)kind)) throw new InvalidDataException($"Terrain inconnu : {kind}");
                var (x, y, x2, y2, radius) = (Finite(r), Finite(r), Finite(r), Finite(r), Finite(r));
                var mines = Count(r);
                if (mines > MaxMines || radius < 0) throw new InvalidDataException("Pièce de terrain illisible");
                pieces.Add(new TerrainPatch(id, (TerrainKind)kind, x, y, radius) { X2 = x2, Y2 = y2, Mines = mines, MaxMines = mines });
            }
            replica.Terrain.Clear();
            replica.Terrain.AddRange(pieces);
        }
        var fields = Count(r);
        if (fields > MaxTerrain) throw new InvalidDataException($"Terrain de {fields} champs de mines");
        var k = 0;
        foreach (var p in replica.Terrain)
        {
            if (p.Kind != TerrainKind.Minefield) continue;
            if (k >= fields) break;
            p.Mines = Math.Min(Count(r), p.MaxMines);
            k++;
        }
        // Counts for pieces this replica does not have: read past them.
        for (; k < fields; k++) Count(r);
    }

    /// <summary>The most terrain pieces, and mines in a field, a snapshot may bring: far more than any map has.</summary>
    const int MaxTerrain = 256, MaxMines = 1024;

    static float Finite(BinaryReader r) => r.ReadSingle() is var v && float.IsFinite(v) ? v : throw new InvalidDataException("Nombre illisible");

    /// <summary>
    /// The entities (see WriteDelta): the baseline's, but those left out, merged in id order with
    /// those the snapshot brings, each record rebuilt into the frame and read into the replica.
    /// The replica loses what is no longer listed, and its parts of ships go on their ships.
    /// </summary>
    static void ReadEntities(BinaryReader r, World replica, Side viewer, SnapshotFrames.Frame frame, SnapshotFrames.Frame? baseline,
        SnapshotFrames? frames)
    {
        var count = Count(r);
        var removed = ReadIds(r, frames?.Removed ?? []);
        var changes = Count(r);
        // Only the viewer's sight is known here: Vision.IsVisible and WasSeen answer for it as on the host.
        var visible = SightOf(replica.Visible, viewer);
        var seen = SightOf(replica.Seen, viewer);
        var old = baseline?.Ids ?? [];
        var change = changes > 0 ? NextId(r, 0) : int.MaxValue;
        int b = 0, gone = 0;
        while (b < old.Count || change != int.MaxValue)
        {
            var id = b < old.Count ? old[b] : int.MaxValue;
            if (id < change)
            {
                if (gone < removed.Count && removed[gone] == id) gone++;
                else ReadRecord(r, replica, viewer, frame, id, 0, baseline, b, visible, seen);
                b++;
                continue;
            }
            ReadRecord(r, replica, viewer, frame, change, r.ReadByte(), baseline, id == change ? b++ : -1, visible, seen);
            change = --changes > 0 ? NextId(r, change) : int.MaxValue;
        }
        if (gone != removed.Count || frame.Ids.Count != count) throw new InvalidDataException("Entités mal formées");
        List<int>? lost = null;
        foreach (var e in replica.Entities) if (frame.IndexOf(e.Id) < 0) (lost ??= []).Add(e.Id);
        foreach (var id in lost ?? []) replica.Entities.Remove(id);
        Worlds.AttachHardpoints(replica);
    }

    /// <summary>An id after `last`, written as the gap from it.</summary>
    static int NextId(BinaryReader r, int last)
    {
        var gap = r.ReadVarint();
        return gap > 0 && gap < int.MaxValue - last ? last + gap : throw new InvalidDataException($"Écart d'identifiants de {gap}");
    }

    /// <summary>The replica's set of what its viewer sees (or saw), emptied for this snapshot: the replica knows no other side's.</summary>
    static HashSet<int> SightOf(Dictionary<Side, HashSet<int>> sets, Side viewer)
    {
        var set = sets.GetValueOrDefault(viewer) ?? [];
        sets.Clear();
        set.Clear();
        sets[viewer] = set;
        return set;
    }

    /// <summary>
    /// One entity's record, rebuilt into the frame and read into the replica: the segments in
    /// `mask` from the snapshot, the others from the baseline's record (index `at`; -1 for an
    /// entity new to it, which must bring all of them). Segments that its head says it has not
    /// stay empty, and must not come.
    /// </summary>
    static void ReadRecord(BinaryReader r, World replica, Side viewer, SnapshotFrames.Frame frame, int id, int mask,
        SnapshotFrames.Frame? baseline, int at, HashSet<int> visible, HashSet<int> seen)
    {
        if (mask >= 1 << Segments) throw new InvalidDataException($"Segments inconnus : {mask}");
        var e = replica.Entities.Get(id);
        if (e is null)
        {
            e = new Entity { Id = id };
            replica.Entities.Add(e);
        }
        frame.Ids.Add(id);
        BinaryReader From(int k)
        {
            frame.Starts.Add((int)frame.Data.Length);
            if ((mask & 1 << k) != 0) return r;
            if (at < 0 || baseline!.Length(at, k) == 0) throw new InvalidDataException($"Entité {id} incomplète");
            return baseline.At(at, k);
        }
        void None(int k)
        {
            if ((mask & 1 << k) != 0) throw new InvalidDataException($"Entité {id} mal formée");
            frame.Starts.Add((int)frame.Data.Length);
        }
        var s = From(Who);
        var p = s.BaseStream.Position;
        var head = ReadWho(s, e);
        // A side beyond the races' own is one of the seats the snapshot listed: the screen asks for it.
        if (e.Side.Id > (int)Faction.Errant && !replica.Factions.ContainsKey(e.Side)) throw new InvalidDataException($"Camp {e.Side} non listé");
        Copy(s, p, frame);
        var remembered = (head & Remembered) != 0;
        if ((head & Follows) != 0) None(Motion);
        else
        {
            s = From(Motion);
            p = s.BaseStream.Position;
            ReadMotion(s, e, remembered);
            Copy(s, p, frame);
        }
        s = From(Health);
        p = s.BaseStream.Position;
        ReadHealth(s, e, remembered);
        Copy(s, p, frame);
        if (remembered) None(Rest);
        else
        {
            s = From(Rest);
            p = s.BaseStream.Position;
            ReadRest(s, e);
            Copy(s, p, frame);
        }
        frame.Starts.Add((int)frame.Data.Length);
        if (Worlds.Allies(replica, e.Side, viewer)) return;
        if (!remembered) visible.Add(id);
        if (e.Kind != EntityKind.Ship) seen.Add(id);
    }

    /// <summary>Appends to the frame what was just read from `from`, since `start`.</summary>
    static void Copy(BinaryReader from, long start, SnapshotFrames.Frame into)
    {
        var s = from.BaseStream;
        var n = (int)(s.Position - start);
        var at = (int)into.Data.Length;
        into.Data.SetLength(at + n);
        s.Position = start;
        s.ReadExactly(into.Data.GetBuffer().AsSpan(at, n));
    }

    /// <summary>Who an entity is (see WriteRecord); its head byte.</summary>
    static int ReadWho(BinaryReader r, Entity e)
    {
        var head = r.ReadByte();
        e.Kind = Wire.Known<EntityKind>(head & 3);
        e.Size = Wire.Known<SizeClass>(head >> 2 & 3);
        e.Type = ReadType(r);
        if (!Fits(e.Kind, e.Type)) throw new InvalidDataException($"{e.Kind} de type {e.Type}");
        e.Side = r.ReadSide();
        e.Race = r.ReadEnum<Faction>();
        e.Radius = (double)r.ReadHalf();
        e.Level = r.ReadVarint();
        e.ParentId = (head & HasParent) != 0 ? r.ReadVarint() : null;
        if ((head & Follows) != 0)
        {
            if (e.ParentId is null) throw new InvalidDataException($"Pièce {e.Id} sans vaisseau");
            e.Ox = r.ReadSingle();
            e.Oy = r.ReadSingle();
        }
        e.Hero = (head & HasHero) != 0 ? ReadHero(r) : null;
        return head;
    }

    static void ReadMotion(BinaryReader r, Entity e, bool remembered)
    {
        e.X = r.ReadSingle();
        e.Y = r.ReadSingle();
        if (remembered) return;
        e.Angle = r.ReadUInt16() * AngleUnit;
        e.Speed = (double)r.ReadHalf();
    }

    /// <summary>Health; of a remembered entity, only what came (its build, a flagship's hull): the rest stays as last seen.</summary>
    static void ReadHealth(BinaryReader r, Entity e, bool remembered)
    {
        var flags = r.ReadByte();
        if ((flags & HullKnown) != 0)
        {
            e.Hull = r.ReadSingle();
            e.MaxHull = r.ReadSingle();
        }
        if ((flags & Shielded) != 0)
        {
            e.Shield = r.ReadSingle();
            e.MaxShield = r.ReadSingle();
        }
        else if (!remembered) e.Shield = e.MaxShield = 0;
        if ((flags & ShieldIsDown) != 0) e.ShieldDown = r.ReadSingle();
        else if (!remembered) e.ShieldDown = 0;
        e.Build = (flags & Building) != 0 ? r.ReadSingle() : 1;
    }

    static void ReadRest(BinaryReader r, Entity e)
    {
        var has = (Has)r.ReadUInt16();
        var weapons = (has & Has.Weapons) != 0 ? Count(r) : 0;
        if (e.Weapons.Count != weapons) e.Weapons = [.. Enumerable.Range(0, weapons).Select(_ => new WeaponState(Unknown))];
        e.TargetId = (has & Has.Target) != 0 ? r.ReadVarint() : null;
        ReadAbilities(r, e, (has & Has.Abilities) != 0 ? Count(r) : 0);
        var fx = e.Effects;
        if ((has & Has.Effects) != 0)
        {
            fx.Marked = r.ReadSingle();
            fx.Stunned = r.ReadSingle();
            fx.Corroded = r.ReadSingle();
            fx.Taunted = r.ReadSingle();
            fx.TauntedBy = Optional(r);
        }
        else
        {
            fx.Marked = fx.Stunned = fx.Corroded = fx.Taunted = 0;
            fx.TauntedBy = null;
        }
        e.Expires = (has & Has.Expires) != 0 ? r.ReadSingle() : null;
        if ((has & Has.Hangar) != 0)
        {
            var cap = r.ReadVarint();
            if (e.Hangar?.Def.Cap != cap) e.Hangar = new HangarState(new HangarDef([], cap, 0));
            e.Hangar.Launched.Clear();
            for (var n = Count(r); n > 0; n--) e.Hangar.Launched.Add(r.ReadVarint());
        }
        else e.Hangar = null;
        e.Income = (has & Has.Income) != 0 ? r.ReadSingle() : 0;
        if ((has & Has.Engines) != 0)
        {
            e.Engines = r.ReadVarint();
            e.MaxEngines = r.ReadVarint();
        }
        else (e.Engines, e.MaxEngines) = (null, 0);
        e.ShieldGens = (has & Has.ShieldGens) != 0 ? r.ReadVarint() : null;
        e.Launched = (has & Has.Launched) != 0;
        if ((has & Has.Pilot) != 0)
        {
            var p = e.Pilot ??= new Pilot();
            p.Thrust = r.ReadSingle();
            p.Turn = r.ReadSingle();
            p.AimX = r.ReadSingle();
            p.AimY = r.ReadSingle();
            p.Firing = r.ReadBoolean();
        }
        else e.Pilot = null;
        if ((has & Has.Order) != 0)
        {
            var (type, x, y, target) = (r.ReadEnum<OrderType>(), r.ReadSingle(), r.ReadSingle(), r.ReadVarint());
            // The same order as before keeps its object: no garbage for every own ship at every snapshot.
            if (e.Order is not { } o || o.Type != type || o.X != x || o.Y != y || o.TargetId != target
                || (has & Has.Plan) == 0 && o.Route is not null) e.Order = new Order(type, x, y, target);
        }
        else e.Order = null;
        e.Range = (has & Has.Range) != 0 ? r.ReadSingle() : 0;
        if ((has & Has.Rank) != 0)
        {
            var rank = r.ReadByte();
            e.Rank = rank <= Veterans.MaxRank ? rank : throw new InvalidDataException($"Rang {rank}");
            e.Xp = (has & Has.Range) != 0 ? Finite(r) : 0;
        }
        else (e.Rank, e.Xp) = (0, 0);
        e.Stance = (has & Has.Stance) != 0 ? r.ReadEnum<Stance>() : Stance.Defensive;
        if ((has & Has.Plan) != 0) ReadPlan(r, e);
        else e.Queue?.Clear();
    }

    /// <summary>
    /// A patrol's route, onto the order just read, and the orders queued after it. A route the
    /// same as before keeps its array: one is only made when the patrol changed.
    /// </summary>
    static void ReadPlan(BinaryReader r, Entity e)
    {
        var n = r.ReadVarint();
        if (n is < 0 or > Movement.MaxRoute) throw new InvalidDataException($"Patrouille de {n} points");
        Span<Point> route = stackalloc Point[Movement.MaxRoute];
        for (var i = 0; i < n; i++) route[i] = new Point(r.ReadSingle(), r.ReadSingle());
        if (n > 0)
        {
            var leg = r.ReadVarint();
            if (leg < 0 || leg >= n) throw new InvalidDataException($"Étape {leg} d'une patrouille de {n}");
            if (e.Order is { } o && (o.Route is not { } was || !Same(route[..n], was) || o.Leg != leg))
                e.Order = o with { Route = route[..n].ToArray(), Leg = leg };
        }
        var queued = r.ReadVarint();
        if (queued is < 0 or > Movement.MaxQueue) throw new InvalidDataException($"{queued} ordres en attente");
        e.Queue ??= [];
        e.Queue.Clear();
        for (var i = 0; i < queued; i++) e.Queue.Add(new Order(r.ReadEnum<OrderType>(), r.ReadSingle(), r.ReadSingle(), r.ReadVarint()));
    }

    static bool Same(ReadOnlySpan<Point> a, IReadOnlyList<Point> b)
    {
        if (a.Length != b.Count) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Abilities in place when they are the same ones, so that what holds them keeps them.</summary>
    static void ReadAbilities(BinaryReader r, Entity e, int n)
    {
        var same = e.Abilities.Count == n;
        var list = same ? e.Abilities : new List<AbilityState>(n);
        for (var i = 0; i < n; i++)
        {
            var id = r.ReadEnum<AbilityId>();
            AbilityState a;
            if (same && list[i].Id == id) a = list[i];
            else
            {
                a = new AbilityState(id);
                if (same) list[i] = a;
                else list.Add(a);
            }
            a.Cooldown = r.ReadSingle();
            a.Active = r.ReadSingle();
        }
        e.Abilities = list;
    }

    /// <summary>Shots are listed by id, as fired: those still flying keep their object, for the view that follows them.</summary>
    static void ReadShots(BinaryReader r, World replica, SnapshotFrames.Frame frame, SnapshotFrames.Frame? baseline, SnapshotFrames? frames)
    {
        frame.Time = replica.Time;
        var count = Count(r);
        var removed = ReadIds(r, frames?.Removed ?? []);
        var changes = Count(r);
        var old = baseline?.Shots ?? [];
        var dt = replica.Time - (baseline?.Time ?? 0);
        var shots = frame.Shots;
        var change = changes > 0 ? NextId(r, 0) : int.MaxValue;
        int b = 0, gone = 0;
        while (b < old.Count || change != int.MaxValue)
        {
            var id = b < old.Count ? old[b].Id : int.MaxValue;
            if (id < change)
            {
                if (gone < removed.Count && removed[gone] == id) gone++;
                else shots.Add(old[b].After(dt));
                b++;
                continue;
            }
            if (id == change) b++;
            shots.Add(new(change, r.ReadSide().Id, (byte)r.ReadEnum<WeaponKind>(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            change = --changes > 0 ? NextId(r, change) : int.MaxValue;
        }
        if (gone != removed.Count || shots.Count != count) throw new InvalidDataException("Tirs mal formés");
        // Shots still flying keep their object, for the view that follows them.
        var was = replica.Projectiles;
        var list = new List<Projectile>(shots.Count);
        var j = 0;
        foreach (var s in shots)
        {
            while (j < was.Count && was[j].Id < s.Id) j++;
            var p = j < was.Count && was[j].Id == s.Id ? was[j] : new Projectile { Id = s.Id };
            (p.Side, p.Kind, p.X, p.Y, p.Vx, p.Vy) = (new Side(s.Side), (WeaponKind)s.Kind, s.X, s.Y, s.Vx, s.Vy);
            list.Add(p);
        }
        replica.Projectiles = list;
    }

    /// <summary>Ids written by WriteIds, into a list emptied first.</summary>
    static List<int> ReadIds(BinaryReader r, List<int> into)
    {
        into.Clear();
        var last = 0;
        for (var n = Count(r); n > 0; n--) into.Add(last = NextId(r, last));
        return into;
    }

    static Strike ReadStrike(BinaryReader r) => new()
    {
        Kind = r.ReadEnum<StrikeKind>(), Side = r.ReadSide(),
        X = r.ReadSingle(), Y = r.ReadSingle(), X2 = r.ReadSingle(), Y2 = r.ReadSingle(),
        Radius = r.ReadSingle(), Damage = r.ReadSingle(), Delay = r.ReadSingle(), Total = r.ReadSingle(),
    };

    static string ReadType(BinaryReader r)
    {
        var code = r.ReadByte();
        // A type by name is still one of the game's: the table holds them all, the name only spells one out.
        var type = code == NamedType ? r.ReadText(64) : code < Types.Length ? Types[code] : null;
        return type is not null && TypeCodes.ContainsKey(type) ? type : throw new InvalidDataException($"Type inconnu : {code}");
    }

    /// <summary>Whether an entity of this kind may be of this type: a ship class, a part, a station or a structure that is built.</summary>
    static bool Fits(EntityKind kind, string type) => kind switch
    {
        EntityKind.Ship => Ids.IsShip(type),
        EntityKind.Hardpoint => Ids.IsRole(type),
        _ => type == "station" || Ids.IsBuildable(type, out _),
    };

    static SimEvent ReadEvent(BinaryReader r) => (Tag)r.ReadByte() switch
    {
        Tag.Destroyed => new Destroyed(r.ReadVarint(), r.ReadEnum<EntityKind>(), ReadType(r), r.ReadSide(),
            r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
        Tag.Hit => new Hit(r.ReadSingle(), r.ReadSingle(), r.ReadEnum<WeaponKind>(), r.ReadVarint(), r.ReadSide(), Optional(r)),
        Tag.Built => new Built(r.ReadVarint()),
        Tag.LanceOnline => new LanceOnline(r.ReadVarint(), r.ReadSide()),
        Tag.Delivered => new Delivered(r.ReadVarint()),
        Tag.Researched => new Researched(r.ReadSide(), r.ReadEnum<TechId>()),
        Tag.AbilityUsed => new AbilityUsed(r.ReadEnum<AbilityId>(), r.ReadSide(), r.ReadVarint(),
            r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
        Tag.StrikeLanded => new StrikeLanded(ReadStrike(r)),
        Tag.Rebuilt => new Rebuilt(r.ReadVarint(), r.ReadSide(), r.ReadEnum<HardpointRole>()),
        Tag.Victory => new Victory(r.ReadSide(), r.ReadVarint()),
        Tag.RaidWon => new RaidWon(r.ReadSide(), r.ReadDouble(), r.ReadEnumOrNone<TechId>()),
        Tag.WaveIncoming => new WaveIncoming(r.ReadVarint(), r.ReadSingle(), r.ReadSingle(), r.ReadEnumOrNone<ShipClass>()),
        Tag.WaveCleared => new WaveCleared(r.ReadVarint(), r.ReadVarint()),
        Tag.BeaconTaken => new BeaconTaken(r.ReadVarint(), r.ReadSide()),
        Tag.BeaconLost => new BeaconLost(r.ReadVarint(), r.ReadSide()),
        Tag.FlagshipLost => new FlagshipLost(r.ReadSide(), r.ReadSingle(), r.ReadSingle()),
        Tag.ClockWarning => new ClockWarning(r.ReadSingle()),
        Tag.Intercepted => new Intercepted(r.ReadSingle(), r.ReadSingle(), r.ReadSide()),
        Tag.MineBlast => new MineBlast(r.ReadSingle(), r.ReadSingle()),
        Tag.Promoted => new Promoted(r.ReadVarint(), r.ReadSide(), r.ReadByte() is var rank && rank is >= 1 and <= Veterans.MaxRank ? rank : throw new InvalidDataException($"Rang {rank}")),
        var tag => throw new InvalidDataException($"Événement inconnu : {tag}"),
    };
}
