// Battle snapshots (not in the web version): what one side's team may see of a battle, written
// by the host for each player and read into the player's own copy of the world, its replica.
// No step ever runs on a replica: it only changes when a snapshot is read into it. Every
// snapshot is whole for now; deltas can come later. The writer is here, the reader in
// SnapshotRead.cs.
//
// Layout, little-endian; "n" is a 7-bit encoded int (ids, counts, sides, levels):
//   header    u32 magic "SSNP", u16 version, n viewer
//   world     f64 time, f64 width, f64 height, u8 flags (fog, sandbox tools, winner), [n winner], f64 last damage
//   mode      n length, then the battle mode's state (see WriteMode): u8 kind and that mode's own fields
//   sides     n count; each: n side, u8 race, n team, u32 color, u8 flags (had a station, has one, viewer),
//             [n station id], n built, n lost, n kills, f32 damage, f32 earned; the viewer's own also
//             its economy (credits, queues, research, loops, hero delays, rally point)
//   fields    n count; each: n id, f32 x, f32 y, f32 radius, n mine id (-1: none)
//   entities  n count; each: see WriteEntity. Foes the viewer's team does not see are left out,
//             but for structures seen before (remembered), sent with only their place and level, and
//             in regicide the flagships, whose place everyone knows (sent the same way, without their parts)
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
    /// included: a reader refuses any other version. 2: the battle modes' state and events.
    /// </summary>
    public const ushort Version = 2;
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

    // Entity head byte: kind (2 bits), size (2), then these.
    const int Remembered = 1 << 4, HasHero = 1 << 5, HasParent = 1 << 6, Building = 1 << 7;

    // Entity flags, for what is often missing.
    [Flags]
    enum Has : ushort
    {
        Speed = 1, Shield = 1 << 1, Weapons = 1 << 2, Target = 1 << 3, Abilities = 1 << 4, Effects = 1 << 5, ShieldDown = 1 << 6,
        Expires = 1 << 7, Hangar = 1 << 8, Income = 1 << 9, Engines = 1 << 10, ShieldGens = 1 << 11, Launched = 1 << 12,
        // Only for the viewer's own entities.
        Pilot = 1 << 13, Order = 1 << 14, Range = 1 << 15,
    }

    enum Tag : byte
    {
        Destroyed = 1, Hit, Built, Delivered, Researched, AbilityUsed, StrikeLanded, Rebuilt, Victory, RaidWon, LanceOnline,
        WaveIncoming, WaveCleared, BeaconTaken, BeaconLost, FlagshipLost, ClockWarning,
    }

    /// <summary>
    /// Writes what `viewer`'s team may see of the battle. `events` are those since the previous
    /// snapshot to this viewer; by default the world's own, which the view clears every frame.
    /// </summary>
    public static void Write(World world, Side viewer, BinaryWriter w, IReadOnlyList<SimEvent>? events = null)
    {
        w.Write(Magic);
        w.Write(Version);
        w.Write7BitEncodedInt(viewer.Id);
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
        var shown = WriteEntities(world, viewer, w);
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
        w.Write7BitEncodedInt(told.Count);
        foreach (var ev in told) WriteEvent(world, w, ev, shown);
    }

    /// <summary>
    /// u8 kind, then that mode's fields; the others keep their defaults on the replica (the
    /// escorts of regicide stay the host's: only its AI needs them).
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
    /// foe flagship out of sight goes as a remembered structure does, its place only, for the
    /// minimap that shows every flagship. Returns the ids written.
    /// </summary>
    static HashSet<int> WriteEntities(World world, Side viewer, BinaryWriter w)
    {
        var shown = new HashSet<int>();
        HashSet<int>? placed = null;
        var list = new List<(Entity E, bool Remembered)>();
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
            list.Add((e, remembered));
        }
        w.Write7BitEncodedInt(list.Count);
        foreach (var (e, remembered) in list) WriteEntity(w, e, viewer, remembered);
        // A flagship known only by its place tells nothing of what it does.
        if (placed is not null) shown.ExceptWith(placed);
        return shown;
    }

    /// <summary>
    /// n id, u8 head (kind, size, remembered, hero, parent, building), u8 type (255: then its name),
    /// n side, u8 race, f32 x, y, f16 radius, n level, [n parent], [u8 hero race, u8 role], [f32 build];
    /// a remembered structure stops there. Then u16 angle, u16 flags (Has) and what they announce,
    /// and f32 hull, max hull.
    /// </summary>
    static void WriteEntity(BinaryWriter w, Entity e, Side viewer, bool remembered)
    {
        w.Write7BitEncodedInt(e.Id);
        var head = (int)e.Kind | (int)e.Size << 2 | (remembered ? Remembered : 0) | (e.Hero is not null ? HasHero : 0)
            | (e.ParentId is not null ? HasParent : 0) | (e.Build < 1 ? Building : 0);
        w.Write((byte)head);
        WriteType(w, e.Type);
        w.Write7BitEncodedInt(e.Side.Id);
        w.Write((byte)e.Race);
        w.Write((float)e.X);
        w.Write((float)e.Y);
        w.Write((Half)e.Radius);
        w.Write7BitEncodedInt(e.Level);
        if (e.ParentId is int parent) w.Write7BitEncodedInt(parent);
        if (e.Hero is HeroId hero) WriteHero(w, hero);
        if (e.Build < 1) w.Write((float)e.Build);
        if (remembered) return;
        w.Write(Angle(e.Angle));
        var own = e.Side == viewer;
        var fx = e.Effects;
        var has = (e.Speed != 0 ? Has.Speed : 0) | (e.MaxShield > 0 ? Has.Shield : 0) | (e.Weapons.Count > 0 ? Has.Weapons : 0)
            | (e.TargetId is not null ? Has.Target : 0) | (e.Abilities.Count > 0 ? Has.Abilities : 0)
            | (fx.Marked > 0 || fx.Stunned > 0 || fx.Corroded > 0 || fx.Taunted > 0 ? Has.Effects : 0)
            | (e.ShieldDown > 0 ? Has.ShieldDown : 0) | (e.Expires is not null ? Has.Expires : 0) | (e.Hangar is not null ? Has.Hangar : 0)
            | (e.Income > 0 ? Has.Income : 0) | (e.Engines is not null ? Has.Engines : 0) | (e.ShieldGens is not null ? Has.ShieldGens : 0)
            | (e.Launched ? Has.Launched : 0)
            | (own && e.Pilot is not null ? Has.Pilot : 0) | (own && e.Order is not null ? Has.Order : 0) | (own ? Has.Range : 0);
        w.Write((ushort)has);
        if (has.HasFlag(Has.Speed)) w.Write((Half)e.Speed);
        if (has.HasFlag(Has.Shield))
        {
            w.Write((float)e.Shield);
            w.Write((float)e.MaxShield);
        }
        if (has.HasFlag(Has.Weapons)) w.Write7BitEncodedInt(e.Weapons.Count);
        if (e.TargetId is int target) w.Write7BitEncodedInt(target);
        if (has.HasFlag(Has.Abilities))
        {
            w.Write7BitEncodedInt(e.Abilities.Count);
            foreach (var a in e.Abilities)
            {
                w.Write((byte)a.Id);
                w.Write((float)a.Cooldown);
                w.Write((float)a.Active);
            }
        }
        if (has.HasFlag(Has.Effects))
        {
            w.Write((float)fx.Marked);
            w.Write((float)fx.Stunned);
            w.Write((float)fx.Corroded);
            w.Write((float)fx.Taunted);
            w.Write7BitEncodedInt(fx.TauntedBy ?? -1);
        }
        if (has.HasFlag(Has.ShieldDown)) w.Write((float)e.ShieldDown);
        if (e.Expires is double expires) w.Write((float)expires);
        if (e.Hangar is { } hangar)
        {
            w.Write7BitEncodedInt(hangar.Def.Cap);
            w.Write7BitEncodedInt(hangar.Launched.Count);
            foreach (var id in hangar.Launched) w.Write7BitEncodedInt(id);
        }
        if (has.HasFlag(Has.Income)) w.Write((float)e.Income);
        if (e.Engines is int engines)
        {
            w.Write7BitEncodedInt(engines);
            w.Write7BitEncodedInt(e.MaxEngines);
        }
        if (e.ShieldGens is int gens) w.Write7BitEncodedInt(gens);
        if (has.HasFlag(Has.Pilot))
        {
            var p = e.Pilot!;
            w.Write((float)p.Thrust);
            w.Write((float)p.Turn);
            w.Write((float)p.AimX);
            w.Write((float)p.AimY);
            w.Write(p.Firing);
        }
        if (has.HasFlag(Has.Order))
        {
            var o = e.Order!;
            w.Write((byte)o.Type);
            w.Write((float)o.X);
            w.Write((float)o.Y);
            w.Write7BitEncodedInt(o.TargetId);
        }
        if (has.HasFlag(Has.Range)) w.Write((float)e.Range);
        w.Write((float)e.Hull);
        w.Write((float)e.MaxHull);
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
