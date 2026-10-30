// Battle snapshots (not in the web version): what one side's team may see of a battle, written
// by the host for each player and read into the player's own copy of the world, its replica.
// No step ever runs on a replica: it only changes when a snapshot is read into it. Entities go
// as deltas: a snapshot is written against one the player acknowledged (SnapshotFrames.cs),
// with only the entities that changed since, and of each only the segments of its record that
// changed; the rest of the snapshot is whole. The writer is here, the reader in SnapshotRead.cs.
//
// Layout, little-endian; "n" is a 7-bit encoded int (ids, counts, sides, levels):
//   header    u32 magic "SSNP", u16 version, n viewer, n snapshot number (0: not kept), n baseline (0: none, all whole)
//   world     f64 time, f64 width, f64 height, u8 flags (fog, sandbox tools, winner), [n winner], f64 last damage
//   mode      n length, then the battle mode's state (see WriteMode): u8 kind and that mode's own fields
//   sides     n count; each: n side, u8 race, n team, u32 color, u8 flags (had a station, has one, viewer),
//             [n station id], n built, n lost, n kills, f32 damage, f32 earned; the viewer's own also
//             its economy (credits, queues, research, loops, hero delays, rally point)
//   fields    n count; each: n id, f32 x, f32 y, f32 radius, n mine id (-1: none)
//   entities  n count in this snapshot; n left out since the baseline, each its id as a gap from the last;
//             n new or changed, each: its id as a gap, u8 segments that follow, then those segments of its
//             record (see WriteRecord). Foes the viewer's team does not see are left out, but for structures
//             seen before (remembered), sent with only their place and level, and in regicide the flagships,
//             whose place and hull everyone knows (sent the same way, with their hull, without their parts)
//   shots     n count; each: n id, n side, u8 kind, f32 x, y, vx, vy. Those far from any eye of the team are left out
//   zones     n count; each: u8 kind, n side, f32 x, y, r
//   strikes   n count; each: see WriteStrike. All of them: every side sees a blow coming
//   events    n count; each: u8 tag and its fields (see WriteEvent), those the viewer may know of
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

/// <summary>On a replica, the host's Built for a star lance out of sight: everyone hears of one, wherever it stands.</summary>
public sealed record LanceOnline(int Id, Side Side) : SimEvent;

public static partial class Snapshot
{
    /// <summary>
    /// Bumped with any change to the layout, the type table (a new ship class, structure or part)
    /// included: a reader refuses any other version. 2: the battle modes' state and events. 3: deltas,
    /// records in segments, the parts of a ship placed by the reader.
    /// </summary>
    public const ushort Version = 3;
    const uint Magic = 0x504E5353;

    /// <summary>Room around the team's sight within which shots and blows still show: they fly in from the dark.</summary>
    const double SightMargin = 150;

    /// <summary>Entity types by code: one byte each instead of the name. A name not in it goes as text after code 255.</summary>
    static readonly string[] Types =
        ["station", .. Enum.GetValues<ShipClass>().Select(Ids.Of), .. Enum.GetValues<BuildableType>().Select(Ids.Of), .. Enum.GetValues<HardpointRole>().Select(Ids.Of)];
    static readonly Dictionary<string, byte> TypeCodes = Types.Select((t, i) => (t, (byte)i)).ToDictionary(p => p.t, p => p.Item2);
    const byte NamedType = 255;

    /// <summary>Angles go as 16 bits: a ten-thousandth of a turn is finer than any pixel.</summary>
    const double AngleUnit = Math.Tau / 65536;

    // Entity head byte: kind (2 bits), size (2), then these. A part that follows a ship has no place
    // of its own: the reader puts it on its ship, as a step does (Worlds.AttachHardpoints).
    const int Remembered = 1 << 4, HasHero = 1 << 5, HasParent = 1 << 6, Follows = 1 << 7;

    // Segments of an entity's record (see WriteRecord), as bits of the mask of a changed entity.
    const int Who = 0, Motion = 1, Health = 2, Rest = 3, Segments = SnapshotFrames.Segments;

    // Health flags.
    const int HullKnown = 1, Shielded = 2, ShieldIsDown = 4, Building = 8;

    // Flags of the rest of a record, for what is often missing. Tested with masks: Enum.HasFlag boxed.
    [Flags]
    enum Has : ushort
    {
        Weapons = 1, Target = 1 << 1, Abilities = 1 << 2, Effects = 1 << 3, Expires = 1 << 4, Hangar = 1 << 5, Income = 1 << 6,
        Engines = 1 << 7, ShieldGens = 1 << 8, Launched = 1 << 9,
        // Only for the viewer's own entities.
        Pilot = 1 << 10, Order = 1 << 11, Range = 1 << 12,
    }

    enum Tag : byte
    {
        Destroyed = 1, Hit, Built, Delivered, Researched, AbilityUsed, StrikeLanded, Rebuilt, Victory, RaidWon, LanceOnline,
        WaveIncoming, WaveCleared, BeaconTaken, BeaconLost, FlagshipLost, ClockWarning,
    }

    /// <summary>
    /// Events whose loss would leave the player without the end of the game or a notice it must
    /// see: victory (a battle mode's end included), the raid, the modes' news, a station falling
    /// (a defeat in a free-for-all), a star lance coming online or firing. Over the network they go
    /// on the reliable channel (`vital` in Write), not in the snapshot: one lost fragment of a
    /// snapshot past ENet's MTU drops it whole.
    /// </summary>
    public static bool Vital(World world, SimEvent ev) => ev switch
    {
        Victory or RaidWon or WaveIncoming or WaveCleared or BeaconTaken or BeaconLost or FlagshipLost or ClockWarning or LanceOnline => true,
        Destroyed d => d.EntityType == "station",
        AbilityUsed a => a.Id == AbilityId.StarLance,
        Built b => world.Entities.Get(b.Id) is { Type: "starLance" },
        _ => false,
    };

    /// <summary>
    /// Writes what `viewer`'s team may see of the battle. `events` are those since the previous
    /// snapshot to this viewer; by default the world's own, which the view clears every frame.
    /// With `vital`, the events that must reach the player (Vital) go there instead, a count then
    /// the events, for the reliable channel; ReadEvents reads them. With `frames`, those sent to
    /// this player, the snapshot is their next one, written against the newest the player
    /// acknowledged; without, it goes whole and is not kept.
    /// </summary>
    public static void Write(World world, Side viewer, BinaryWriter w, IReadOnlyList<SimEvent>? events = null, BinaryWriter? vital = null,
        SnapshotFrames? frames = null)
    {
        var (frame, baseline) = frames?.Next() ?? (new SnapshotFrames.Frame(), null);
        w.Write(Magic);
        w.Write(Version);
        w.Write7BitEncodedInt(viewer.Id);
        w.Write7BitEncodedInt(frame.Seq);
        w.Write7BitEncodedInt(baseline?.Seq ?? 0);
        w.Write(world.Time);
        w.Write(world.Width);
        w.Write(world.Height);
        w.Write((byte)((world.Fog ? 1 : 0) | (world.SandboxTools ? 2 : 0) | (world.Winner is not null ? 4 : 0)));
        if (world.Winner is Side winner) w.Write7BitEncodedInt(winner.Id);
        w.Write(world.LastDamage);
        // The battle mode's state, its length first: a reader checks it took all of it.
        var mode = ModeBytes(world.Mode);
        w.Write7BitEncodedInt(mode.Length);
        w.Write(mode);
        WriteSides(world, viewer, w);
        w.Write7BitEncodedInt(world.Fields.Count);
        foreach (var f in world.Fields)
        {
            w.Write7BitEncodedInt(f.Id);
            w.Write((float)f.X);
            w.Write((float)f.Y);
            w.Write((float)f.Radius);
            w.Write7BitEncodedInt(f.MineId ?? -1);
        }
        var shown = WriteEntities(world, viewer, w, frame, baseline, frames);
        frames?.Keep(frame);
        var sight = new Sight(world, viewer);
        WriteShots(world, viewer, sight, w);
        var zones = world.Zones.Where(z => Allies(world, z.Side, viewer) || sight.Sees(z.X, z.Y)).ToList();
        w.Write7BitEncodedInt(zones.Count);
        foreach (var z in zones)
        {
            w.Write((byte)z.Kind);
            w.Write7BitEncodedInt(z.Side.Id);
            w.Write((float)z.X);
            w.Write((float)z.Y);
            w.Write((float)z.R);
        }
        w.Write7BitEncodedInt(world.Strikes.Count);
        foreach (var s in world.Strikes) WriteStrike(w, s);
        var told = (events ?? world.Events).Where(ev => Tells(world, viewer, ev, shown, sight)).ToList();
        var kept = vital is null ? told : told.Where(ev => !Vital(world, ev)).ToList();
        w.Write7BitEncodedInt(kept.Count);
        foreach (var ev in kept) WriteEvent(world, w, ev, shown);
        if (vital is null) return;
        var sent = told.Where(ev => Vital(world, ev)).ToList();
        vital.Write7BitEncodedInt(sent.Count);
        foreach (var ev in sent) WriteEvent(world, vital, ev, shown);
    }

    /// <summary>
    /// u8 kind, then that mode's fields; the others keep their defaults on the replica (the
    /// escorts and worn hulls of regicide stay the host's: only its AI and its sudden death need them).
    ///   survival  n wave, n cleared, f64 next wave (infinite after the last one)
    ///   zones     n target; n beacons, each: f32 x, y, n holder + 1 (0: nobody), n claim + 1, f32 progress, u8 contested;
    ///             n scores, each: n side, f64 points
    ///   assault   f64 deadline
    ///   regicide  n flagships, each: n side, n entity id
    /// </summary>
    static byte[] ModeBytes(ModeState m)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)m.Kind);
            switch (m.Kind)
            {
                case BattleMode.Survival:
                    w.Write7BitEncodedInt(m.Wave);
                    w.Write7BitEncodedInt(m.Cleared);
                    w.Write(m.NextWave);
                    break;
                case BattleMode.Zones:
                    w.Write7BitEncodedInt(m.ZoneTarget);
                    w.Write7BitEncodedInt(m.Beacons.Count);
                    foreach (var b in m.Beacons)
                    {
                        w.Write((float)b.X);
                        w.Write((float)b.Y);
                        w.Write7BitEncodedInt(b.Holder is Side h ? h.Id + 1 : 0);
                        w.Write7BitEncodedInt(b.Claim is Side c ? c.Id + 1 : 0);
                        w.Write((float)b.Progress);
                        w.Write(b.Contested);
                    }
                    w.Write7BitEncodedInt(m.Scores.Count);
                    foreach (var (side, points) in m.Scores)
                    {
                        w.Write7BitEncodedInt(side.Id);
                        w.Write(points);
                    }
                    break;
                case BattleMode.Assault:
                    w.Write(m.Deadline);
                    break;
                case BattleMode.Regicide:
                    w.Write7BitEncodedInt(m.Flagships.Count);
                    foreach (var (side, id) in m.Flagships)
                    {
                        w.Write7BitEncodedInt(side.Id);
                        w.Write7BitEncodedInt(id);
                    }
                    break;
            }
        }
        return ms.ToArray();
    }

    static void WriteSides(World world, Side viewer, BinaryWriter w)
    {
        w.Write7BitEncodedInt(world.Factions.Count);
        foreach (var f in world.Factions.Values)
        {
            var own = f.Id == viewer;
            w.Write7BitEncodedInt(f.Id.Id);
            w.Write((byte)f.Race);
            w.Write7BitEncodedInt(f.Team);
            w.Write(f.Color);
            w.Write((byte)((f.HadStation ? 1 : 0) | (f.StationId is not null ? 2 : 0) | (own ? 4 : 0)));
            if (f.StationId is int station) w.Write7BitEncodedInt(station);
            var s = f.Stats;
            w.Write7BitEncodedInt(s.Built);
            w.Write7BitEncodedInt(s.Lost);
            w.Write7BitEncodedInt(s.Kills);
            w.Write((float)s.Damage);
            w.Write((float)s.Earned);
            if (own) WriteEconomy(f, w);
        }
    }

    /// <summary>The viewer's own side in full: what its HUD shows and checks before sending an order.</summary>
    static void WriteEconomy(FactionState f, BinaryWriter w)
    {
        w.Write(f.Credits);
        w.Write(f.Bounties);
        w.Write(f.AutoAbilities);
        w.Write((float)f.RallyX);
        w.Write((float)f.RallyY);
        WriteQueue(w, f.Queue);
        WriteQueue(w, f.BuildQueue);
        w.Write7BitEncodedInt(Techs.All.Count);
        foreach (var t in Techs.All) w.Write7BitEncodedInt(f.Research[t]);
        w.Write(f.Researching is not null);
        if (f.Researching is { } job) WriteJob(w, job);
        w.Write7BitEncodedInt(f.ResearchQueue.Count);
        foreach (var j in f.ResearchQueue) WriteJob(w, j);
        w.Write7BitEncodedInt(f.Repeat.Count);
        foreach (var c in f.Repeat) w.Write((byte)c);
        w.Write7BitEncodedInt(f.HeroCooldowns.Count);
        foreach (var (hero, wait) in f.HeroCooldowns)
        {
            WriteHero(w, hero);
            w.Write((float)wait);
        }
    }

    static void WriteQueue(BinaryWriter w, List<QueueItem> queue)
    {
        w.Write7BitEncodedInt(queue.Count);
        foreach (var q in queue)
        {
            w.Write((byte)q.Kind);
            w.Write((byte)q.Cls);
            w.Write7BitEncodedInt(q.TargetId);
            WriteHero(w, q.Hero);
            w.Write((float)q.Remaining);
            w.Write((float)q.Total);
            w.Write((float)q.Cost);
        }
    }

    static void WriteJob(BinaryWriter w, ResearchJob job)
    {
        w.Write((byte)job.Tech);
        w.Write((float)job.Remaining);
        w.Write((float)job.Total);
        w.Write((float)job.Cost);
    }

    static void WriteHero(BinaryWriter w, HeroId h)
    {
        w.Write((byte)h.Faction);
        w.Write((byte)h.Role);
    }

    /// <summary>
    /// What the viewer's team sees, as the view shows it: its own and its allies' entities, the
    /// foes it sees, and the foes' structures it saw once (remembered). A part goes with what
    /// it is attached to: never the turrets of a ship that is hidden or cloaked. In regicide, a
    /// foe flagship out of sight goes as a remembered structure does, by its place and its hull,
    /// for the minimap and the mode panel that show every flagship. Each record goes into the
    /// snapshot's frame; the snapshot gets those that changed since the baseline. Returns the ids written.
    /// </summary>
    static HashSet<int> WriteEntities(World world, Side viewer, BinaryWriter w, SnapshotFrames.Frame frame, SnapshotFrames.Frame? baseline,
        SnapshotFrames? frames)
    {
        var shown = frames?.Shown ?? [];
        var list = frames?.Listed ?? [];
        shown.Clear();
        list.Clear();
        HashSet<int>? placed = null;
        var sorted = true;
        foreach (var e in world.Entities)
        {
            if (e.ParentId is int parent && (!shown.Contains(parent) || placed?.Contains(parent) == true)) continue;
            bool remembered;
            if (Vision.IsVisible(world, viewer, e)) remembered = false;
            else if (e.Kind != EntityKind.Ship && Vision.WasSeen(world, viewer, e)) remembered = true;
            else if (e.Kind == EntityKind.Ship && world.Mode.Flagships.ContainsValue(e.Id))
            {
                remembered = true;
                (placed ??= []).Add(e.Id);
            }
            else continue;
            shown.Add(e.Id);
            if (list.Count > 0 && list[^1].E.Id > e.Id) sorted = false;
            list.Add((e, remembered));
        }
        // By increasing ids, as deltas walk them: a replica holds its entities in the order they came into its sight.
        if (!sorted) list.Sort((a, b) => a.E.Id.CompareTo(b.E.Id));
        foreach (var (e, remembered) in list) WriteRecord(frame, world, e, viewer, remembered);
        WriteDelta(w, frame, baseline, frames);
        // A flagship known only by its place tells nothing of what it does.
        if (placed is not null) shown.ExceptWith(placed);
        return shown;
    }

    /// <summary>
    /// The entities of the frame against the baseline's: n count, the ids of the baseline left
    /// out, then each entity new or changed with the segments that differ (a new one, all of
    /// them). Without a baseline, every entity goes whole.
    /// </summary>
    static void WriteDelta(BinaryWriter w, SnapshotFrames.Frame frame, SnapshotFrames.Frame? baseline, SnapshotFrames? frames)
    {
        var masks = frames?.Masks ?? [];
        var removed = frames?.Removed ?? [];
        masks.Clear();
        removed.Clear();
        var changed = 0;
        var j = 0;
        var old = baseline?.Ids ?? [];
        for (var i = 0; i < frame.Ids.Count; i++)
        {
            var id = frame.Ids[i];
            while (j < old.Count && old[j] < id) removed.Add(old[j++]);
            var b = j < old.Count && old[j] == id ? j++ : -1;
            var mask = 0;
            for (var k = 0; k < Segments; k++)
            {
                if (frame.Length(i, k) > 0 && (b < 0 || !frame.Segment(i, k).SequenceEqual(baseline!.Segment(b, k)))) mask |= 1 << k;
            }
            masks.Add((byte)mask);
            if (mask != 0) changed++;
        }
        while (j < old.Count) removed.Add(old[j++]);
        w.Write7BitEncodedInt(frame.Ids.Count);
        w.Write7BitEncodedInt(removed.Count);
        var last = 0;
        foreach (var id in removed)
        {
            w.Write7BitEncodedInt(id - last);
            last = id;
        }
        w.Write7BitEncodedInt(changed);
        last = 0;
        for (var i = 0; i < frame.Ids.Count; i++)
        {
            if (masks[i] == 0) continue;
            w.Write7BitEncodedInt(frame.Ids[i] - last);
            last = frame.Ids[i];
            w.Write(masks[i]);
            for (var k = 0; k < Segments; k++) if ((masks[i] & 1 << k) != 0) w.Write(frame.Segment(i, k));
        }
    }

    /// <summary>
    /// An entity's record, into the frame, in four segments that change apart. A segment left out
    /// is empty: the place of a part that follows a ship, the rest of a remembered entity.
    ///   who     u8 head (kind, size, remembered, hero, parent, follows), u8 type (255: then its name), n side,
    ///           u8 race, f16 radius, n level, [n parent], [f32 offset x, y on a ship], [u8 hero race, u8 role]
    ///   motion  f32 x, y; but for a remembered entity, u16 angle, f16 speed
    ///   health  u8 flags, [f32 hull, max hull] (but for a remembered structure), [f32 shield, max shield],
    ///           [f32 shield down], [f32 build]
    ///   rest    u16 flags (Has) and what they announce
    /// </summary>
    static void WriteRecord(SnapshotFrames.Frame frame, World world, Entity e, Side viewer, bool remembered)
    {
        if (frame.Ids.Count > 0 && frame.Ids[^1] >= e.Id) throw new InvalidOperationException("Entities out of order");
        frame.Ids.Add(e.Id);
        var w = frame.Writer;
        var follows = e.ParentId is int parent && world.Entities.Get(parent) is { Kind: EntityKind.Ship };
        frame.Starts.Add((int)frame.Data.Length);
        w.Write((byte)((int)e.Kind | (int)e.Size << 2 | (remembered ? Remembered : 0) | (e.Hero is not null ? HasHero : 0)
            | (e.ParentId is not null ? HasParent : 0) | (follows ? Follows : 0)));
        WriteType(w, e.Type);
        w.Write7BitEncodedInt(e.Side.Id);
        w.Write((byte)e.Race);
        w.Write((Half)e.Radius);
        w.Write7BitEncodedInt(e.Level);
        if (e.ParentId is int p) w.Write7BitEncodedInt(p);
        if (follows)
        {
            w.Write((float)e.Ox);
            w.Write((float)e.Oy);
        }
        if (e.Hero is HeroId hero) WriteHero(w, hero);
        frame.Starts.Add((int)frame.Data.Length);
        if (!follows)
        {
            w.Write((float)e.X);
            w.Write((float)e.Y);
            if (!remembered)
            {
                w.Write(Angle(e.Angle));
                w.Write((Half)e.Speed);
            }
        }
        frame.Starts.Add((int)frame.Data.Length);
        var hull = !remembered || e.Kind == EntityKind.Ship;
        var shielded = !remembered && e.MaxShield > 0;
        var down = !remembered && e.ShieldDown > 0;
        w.Write((byte)((hull ? HullKnown : 0) | (shielded ? Shielded : 0) | (down ? ShieldIsDown : 0) | (e.Build < 1 ? Building : 0)));
        if (hull)
        {
            w.Write((float)e.Hull);
            w.Write((float)e.MaxHull);
        }
        if (shielded)
        {
            w.Write((float)e.Shield);
            w.Write((float)e.MaxShield);
        }
        if (down) w.Write((float)e.ShieldDown);
        if (e.Build < 1) w.Write((float)e.Build);
        frame.Starts.Add((int)frame.Data.Length);
        if (!remembered) WriteRest(w, e, viewer);
        frame.Starts.Add((int)frame.Data.Length);
    }

    static void WriteRest(BinaryWriter w, Entity e, Side viewer)
    {
        var own = e.Side == viewer;
        var fx = e.Effects;
        var has = (e.Weapons.Count > 0 ? Has.Weapons : 0) | (e.TargetId is not null ? Has.Target : 0) | (e.Abilities.Count > 0 ? Has.Abilities : 0)
            | (fx.Marked > 0 || fx.Stunned > 0 || fx.Corroded > 0 || fx.Taunted > 0 ? Has.Effects : 0)
            | (e.Expires is not null ? Has.Expires : 0) | (e.Hangar is not null ? Has.Hangar : 0)
            | (e.Income > 0 ? Has.Income : 0) | (e.Engines is not null ? Has.Engines : 0) | (e.ShieldGens is not null ? Has.ShieldGens : 0)
            | (e.Launched ? Has.Launched : 0)
            | (own && e.Pilot is not null ? Has.Pilot : 0) | (own && e.Order is not null ? Has.Order : 0) | (own ? Has.Range : 0);
        w.Write((ushort)has);
        if ((has & Has.Weapons) != 0) w.Write7BitEncodedInt(e.Weapons.Count);
        if (e.TargetId is int target) w.Write7BitEncodedInt(target);
        if ((has & Has.Abilities) != 0)
        {
            w.Write7BitEncodedInt(e.Abilities.Count);
            foreach (var a in e.Abilities)
            {
                w.Write((byte)a.Id);
                w.Write((float)a.Cooldown);
                w.Write((float)a.Active);
            }
        }
        if ((has & Has.Effects) != 0)
        {
            w.Write((float)fx.Marked);
            w.Write((float)fx.Stunned);
            w.Write((float)fx.Corroded);
            w.Write((float)fx.Taunted);
            w.Write7BitEncodedInt(fx.TauntedBy ?? -1);
        }
        if (e.Expires is double expires) w.Write((float)expires);
        if (e.Hangar is { } hangar)
        {
            w.Write7BitEncodedInt(hangar.Def.Cap);
            w.Write7BitEncodedInt(hangar.Launched.Count);
            foreach (var id in hangar.Launched) w.Write7BitEncodedInt(id);
        }
        if ((has & Has.Income) != 0) w.Write((float)e.Income);
        if (e.Engines is int engines)
        {
            w.Write7BitEncodedInt(engines);
            w.Write7BitEncodedInt(e.MaxEngines);
        }
        if (e.ShieldGens is int gens) w.Write7BitEncodedInt(gens);
        if ((has & Has.Pilot) != 0)
        {
            var p = e.Pilot!;
            w.Write((float)p.Thrust);
            w.Write((float)p.Turn);
            w.Write((float)p.AimX);
            w.Write((float)p.AimY);
            w.Write(p.Firing);
        }
        if ((has & Has.Order) != 0)
        {
            var o = e.Order!;
            w.Write((byte)o.Type);
            w.Write((float)o.X);
            w.Write((float)o.Y);
            w.Write7BitEncodedInt(o.TargetId);
        }
        if ((has & Has.Range) != 0) w.Write((float)e.Range);
    }

    static void WriteType(BinaryWriter w, string type)
    {
        if (TypeCodes.TryGetValue(type, out var code))
        {
            w.Write(code);
            return;
        }
        w.Write(NamedType);
        w.Write(type);
    }

    static ushort Angle(double a) => (ushort)((long)Math.Round(a / AngleUnit) & 0xFFFF);

    static void WriteShots(World world, Side viewer, Sight sight, BinaryWriter w)
    {
        var shots = world.Projectiles.Where(p => Allies(world, p.Side, viewer) || sight.Sees(p.X, p.Y)).ToList();
        w.Write7BitEncodedInt(shots.Count);
        foreach (var p in shots)
        {
            w.Write7BitEncodedInt(p.Id);
            w.Write7BitEncodedInt(p.Side.Id);
            w.Write((byte)p.Kind);
            w.Write((float)p.X);
            w.Write((float)p.Y);
            w.Write((float)p.Vx);
            w.Write((float)p.Vy);
        }
    }

    /// <summary>u8 kind, n side, f32 x, y, x2, y2, radius, damage, delay, total.</summary>
    static void WriteStrike(BinaryWriter w, Strike s)
    {
        w.Write((byte)s.Kind);
        w.Write7BitEncodedInt(s.Side.Id);
        w.Write((float)s.X);
        w.Write((float)s.Y);
        w.Write((float)s.X2);
        w.Write((float)s.Y2);
        w.Write((float)s.Radius);
        w.Write((float)s.Damage);
        w.Write((float)s.Delay);
        w.Write((float)s.Total);
    }

    /// <summary>
    /// Whether the viewer may hear of an event, as the view would show it: what befalls its team,
    /// what happens where its team sees, and what everyone hears of (blows landing, star lances,
    /// the raid, victory). An event of a kind this format does not know is not sent.
    /// </summary>
    static bool Tells(World world, Side viewer, SimEvent ev, HashSet<int> shown, Sight sight) => ev switch
    {
        Destroyed d => Allies(world, d.Side, viewer) || sight.Sees(d.X, d.Y),
        Hit h => Allies(world, h.Side, viewer) || sight.Sees(h.X, h.Y),
        Built b => shown.Contains(b.Id) || world.Entities.Get(b.Id) is { Type: "starLance" },
        LanceOnline => true,
        Delivered d => shown.Contains(d.Id),
        Researched r => Allies(world, r.Side, viewer),
        AbilityUsed a => a.Id == AbilityId.StarLance || Allies(world, a.Side, viewer) || shown.Contains(a.EntityId),
        StrikeLanded or Victory or RaidWon => true,
        // The battle modes' news: waves, beacons, flagships and the assault's clock are everyone's.
        WaveIncoming or WaveCleared or BeaconTaken or BeaconLost or FlagshipLost or ClockWarning => true,
        Rebuilt r => Allies(world, r.Side, viewer),
        _ => false,
    };

    static void WriteEvent(World world, BinaryWriter w, SimEvent ev, HashSet<int> shown)
    {
        switch (ev)
        {
            case Destroyed d:
                w.Write((byte)Tag.Destroyed);
                w.Write7BitEncodedInt(d.Id);
                w.Write((byte)d.Kind);
                WriteType(w, d.EntityType);
                w.Write7BitEncodedInt(d.Side.Id);
                w.Write((float)d.X);
                w.Write((float)d.Y);
                w.Write((float)d.Radius);
                break;
            case Hit h:
                w.Write((byte)Tag.Hit);
                w.Write((float)h.X);
                w.Write((float)h.Y);
                w.Write((byte)h.Kind);
                w.Write7BitEncodedInt(h.TargetId);
                w.Write7BitEncodedInt(h.Side.Id);
                w.Write7BitEncodedInt(h.ShieldId ?? -1);
                break;
            // A star lance out of sight: the replica will not have it, the event says whose it is.
            case Built b when !shown.Contains(b.Id):
                w.Write((byte)Tag.LanceOnline);
                w.Write7BitEncodedInt(b.Id);
                w.Write7BitEncodedInt(world.Entities.Get(b.Id)!.Side.Id);
                break;
            case Built b:
                w.Write((byte)Tag.Built);
                w.Write7BitEncodedInt(b.Id);
                break;
            case LanceOnline l:
                w.Write((byte)Tag.LanceOnline);
                w.Write7BitEncodedInt(l.Id);
                w.Write7BitEncodedInt(l.Side.Id);
                break;
            case Delivered d:
                w.Write((byte)Tag.Delivered);
                w.Write7BitEncodedInt(d.Id);
                break;
            case Researched r:
                w.Write((byte)Tag.Researched);
                w.Write7BitEncodedInt(r.Side.Id);
                w.Write((byte)r.Tech);
                break;
            case AbilityUsed a:
                w.Write((byte)Tag.AbilityUsed);
                w.Write((byte)a.Id);
                w.Write7BitEncodedInt(a.Side.Id);
                w.Write7BitEncodedInt(a.EntityId);
                w.Write((float)a.X);
                w.Write((float)a.Y);
                w.Write((float)a.Tx);
                w.Write((float)a.Ty);
                break;
            case StrikeLanded s:
                w.Write((byte)Tag.StrikeLanded);
                WriteStrike(w, s.Strike);
                break;
            case Rebuilt r:
                w.Write((byte)Tag.Rebuilt);
                w.Write7BitEncodedInt(r.Id);
                w.Write7BitEncodedInt(r.Side.Id);
                w.Write((byte)r.Role);
                break;
            case Victory v:
                w.Write((byte)Tag.Victory);
                w.Write7BitEncodedInt(v.Side.Id);
                w.Write7BitEncodedInt(v.Team);
                break;
            case RaidWon rw:
                w.Write((byte)Tag.RaidWon);
                w.Write7BitEncodedInt(rw.Side.Id);
                w.Write(rw.Credits);
                w.Write(rw.Tech is TechId t ? (byte)t : byte.MaxValue);
                break;
            case WaveIncoming wi:
                w.Write((byte)Tag.WaveIncoming);
                w.Write7BitEncodedInt(wi.Wave);
                w.Write((float)wi.X);
                w.Write((float)wi.Y);
                w.Write(wi.Boss is ShipClass boss ? (byte)boss : byte.MaxValue);
                break;
            case WaveCleared wc:
                w.Write((byte)Tag.WaveCleared);
                w.Write7BitEncodedInt(wc.Wave);
                break;
            case BeaconTaken bt:
                w.Write((byte)Tag.BeaconTaken);
                w.Write7BitEncodedInt(bt.Beacon);
                w.Write7BitEncodedInt(bt.Side.Id);
                break;
            case BeaconLost bl:
                w.Write((byte)Tag.BeaconLost);
                w.Write7BitEncodedInt(bl.Beacon);
                w.Write7BitEncodedInt(bl.Side.Id);
                break;
            case FlagshipLost fl:
                w.Write((byte)Tag.FlagshipLost);
                w.Write7BitEncodedInt(fl.Side.Id);
                w.Write((float)fl.X);
                w.Write((float)fl.Y);
                break;
            case ClockWarning cw:
                w.Write((byte)Tag.ClockWarning);
                w.Write((float)cw.Left);
                break;
        }
    }

    /// <summary>
    /// Where the viewer's team sees right now: the eyes of its entities, binned in cells as wide
    /// as the longest sight, so that a point only asks the eyes of the 3x3 cells around it.
    /// Without fog, everywhere.
    /// </summary>
    sealed class Sight
    {
        readonly bool everywhere;
        readonly double cell;
        readonly Dictionary<(int, int), List<(double X, double Y, double R)>> eyes = [];
        (double X, double Y, double R)? last;

        public Sight(World world, Side viewer)
        {
            everywhere = !world.Fog;
            if (everywhere) return;
            var all = new List<(double X, double Y, double R)>();
            foreach (var e in world.Entities)
            {
                if (!Allies(world, e.Side, viewer)) continue;
                var r = Vision.VisionOf(e);
                if (r > 0) all.Add((e.X, e.Y, r + SightMargin));
            }
            cell = all.Count > 0 ? all.Max(a => a.R) : 1;
            foreach (var a in all)
            {
                var key = Key(a.X, a.Y);
                if (!eyes.TryGetValue(key, out var list)) eyes[key] = list = [];
                list.Add(a);
            }
        }

        (int, int) Key(double x, double y) => ((int)Math.Floor(x / cell), (int)Math.Floor(y / cell));

        public bool Sees(double x, double y)
        {
            if (everywhere) return true;
            // Shots come in streams: the eye that saw the last one likely sees this one.
            if (last is { } l && Hypot(l.X - x, l.Y - y) <= l.R) return true;
            var (cx, cy) = Key(x, y);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!eyes.TryGetValue((cx + dx, cy + dy), out var list)) continue;
                    foreach (var a in list)
                    {
                        if (Hypot(a.X - x, a.Y - y) > a.R) continue;
                        last = a;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
