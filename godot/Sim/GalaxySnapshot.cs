// Galaxy snapshots (not in the web version): what one side's team knows of the campaign,
// written by the host for each player and read into the player's own copy of the galaxy, its
// replica. No step ever runs on a replica: it only changes when a snapshot is read into it.
// Every snapshot is whole, the map's planets and lanes included; deltas can come later. The
// writer is here, the reader in GalaxySnapshotRead.cs.
//
// Layout, little-endian; "n" is a 7-bit encoded int (ids, counts, sides, levels):
//   header    u32 magic "GSNP", u16 version, n viewer
//   galaxy    f64 time, f32 width, f32 height, f32 speed, u8 flags (paused, winner), [n winner]
//   humans    n count; each: n side, string name
//   lanes     n count; each: n a, n b
//   planets   n count; each: see WritePlanet. Ownership is known everywhere, what guards a planet
//             only where the team has eyes (Galaxies.Known); queues, loops and rally points only the viewer's own
//   fleets    n count; each: see WriteFleet. Those the team sees (Galaxies.FleetVisible), and the Errants', whose guard all know
//   sides     n count; each: n side, u8 race, n team, u32 color, u8 flags (eliminated, AI, viewer, grudge), [f32 grudge
//             of an AI against the viewer]; the viewer's own also its economy (credits, research, hero delays)
//   battles   n count; each: see WriteBattle. Those that hold the galaxy (waiting or commanded), and the others the team knows of
//   truces    n count; each: n a, n b, f32 left
//   offers    n count; each: n from, n to, f32 left. Those to or from the viewer
//   incidents n count; each: u8 kind, n planet, n other + 1, f32 left, ships
//   events    n count; each: u8 tag and its fields (see WriteEvent), those the viewer may know of
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim;

/// <summary>The galaxy's pace, as the host sets it for everyone: its speed, and whether it is paused.</summary>
public readonly record struct GalaxyClock(double Speed, bool Paused);

public static partial class GalaxySnapshot
{
    /// <summary>Bumped with any change to the layout: a reader refuses any other version.</summary>
    public const ushort Version = 1;
    const uint Magic = 0x504E5347;

    enum Tag : byte
    {
        BattleStarted = 1, BattleEnded, Captured, PlanetBuilt, TechResearched, BountyPaid, GalaxyVictory, TruceOffered, TruceSigned,
        TruceEnded, IncidentStarted, IncidentEnded, RuinsExplored, LanceFired,
    }

    /// <summary>
    /// Events that must reach the player: every notice the map gives. Over the network they go on
    /// the reliable channel (`vital` in Write), not in the snapshot, which may be lost. A battle
    /// starting is not one: the battles listed in each snapshot say it.
    /// </summary>
    public static bool Vital(GalaxyEvent ev) => ev is not BattleStarted;

    /// <summary>
    /// Writes what `viewer`'s team knows of the galaxy. `events` are those since the previous
    /// snapshot to this viewer, by default the galaxy's own; with `vital`, those that must reach
    /// the player go there instead (a count, then the events), for the reliable channel.
    /// </summary>
    public static void Write(Galaxy g, Side viewer, BinaryWriter w, IReadOnlyList<GalaxyEvent>? events = null, BinaryWriter? vital = null,
        GalaxyClock clock = default)
    {
        w.Write(Magic);
        w.Write(Version);
        w.Write7BitEncodedInt(viewer.Id);
        w.Write(g.Time);
        w.Write((float)g.W);
        w.Write((float)g.H);
        w.Write((float)clock.Speed);
        w.Write((byte)((clock.Paused ? 1 : 0) | (g.Winner is not null ? 2 : 0)));
        if (g.Winner is Side winner) w.Write7BitEncodedInt(winner.Id);
        w.Write7BitEncodedInt(g.Humans.Count);
        foreach (var (side, name) in g.Humans)
        {
            w.Write7BitEncodedInt(side.Id);
            w.Write(name);
        }
        w.Write7BitEncodedInt(g.Lanes.Count);
        foreach (var (a, b) in g.Lanes)
        {
            w.Write7BitEncodedInt(a);
            w.Write7BitEncodedInt(b);
        }
        var seen = VisiblePlanets(g, viewer);
        w.Write7BitEncodedInt(g.Planets.Count);
        foreach (var p in g.Planets) WritePlanet(g, w, p, viewer, seen);
        var fleets = g.Fleets.Values.Where(f => FleetVisible(g, viewer, f, seen) || f.Side == Faction.Errant).ToList();
        w.Write7BitEncodedInt(fleets.Count);
        foreach (var f in fleets) WriteFleet(w, f, viewer);
        WriteSides(g, w, viewer);
        var battles = g.Battles.Where(b => Told(g, b, viewer, seen)).ToList();
        w.Write7BitEncodedInt(battles.Count);
        foreach (var b in battles) WriteBattle(w, b, viewer);
        w.Write7BitEncodedInt(g.Truces.Count);
        foreach (var t in g.Truces)
        {
            w.Write7BitEncodedInt(t.A.Id);
            w.Write7BitEncodedInt(t.B.Id);
            w.Write((float)t.Remaining);
        }
        var offers = g.Offers.Where(o => o.From == viewer || o.To == viewer).ToList();
        w.Write7BitEncodedInt(offers.Count);
        foreach (var o in offers)
        {
            w.Write7BitEncodedInt(o.From.Id);
            w.Write7BitEncodedInt(o.To.Id);
            w.Write((float)o.Remaining);
        }
        w.Write7BitEncodedInt(g.Ongoing.Count);
        foreach (var i in g.Ongoing)
        {
            w.Write((byte)i.Kind);
            w.Write7BitEncodedInt(i.Planet);
            w.Write7BitEncodedInt(i.Other + 1);
            w.Write((float)i.Remaining);
            WriteShips(w, i.Ships);
        }
        var told = (events ?? g.Events).Where(ev => Tells(g, viewer, ev, seen)).ToList();
        var kept = vital is null ? told : told.Where(ev => !Vital(ev)).ToList();
        w.Write7BitEncodedInt(kept.Count);
        foreach (var ev in kept) WriteEvent(w, ev);
        if (vital is null) return;
        var sent = told.Where(Vital).ToList();
        vital.Write7BitEncodedInt(sent.Count);
        foreach (var ev in sent) WriteEvent(vital, ev);
    }

    // Planet flags.
    const int Owned = 1, Seen = 2, Occupied = 4, Own = 8, Rallied = 16;

    /// <summary>
    /// string name, f32 x, y, income, n mine slots, u8 special (255: none), u8 flags, [n owner];
    /// where the team has eyes: n station, mines, platforms, platform tier, lab, repair dock, ion
    /// cannon, star lance, [f32 lance charge], [n occupier, f32 left]; the viewer's own: its two
    /// queues, n loops (each u8 class), [n rally point].
    /// </summary>
    static void WritePlanet(Galaxy g, BinaryWriter w, Planet p, Side viewer, HashSet<int> seen)
    {
        w.Write(p.Name);
        w.Write((float)p.X);
        w.Write((float)p.Y);
        w.Write((float)p.Income);
        w.Write7BitEncodedInt(p.MineSlots);
        w.Write(p.Special is PlanetSpecial s ? (byte)s : byte.MaxValue);
        var known = Known(g, viewer, p, seen);
        var own = p.Owner == viewer;
        var occupied = known && p.Occupation is not null;
        w.Write((byte)((p.Owner is not null ? Owned : 0) | (known ? Seen : 0) | (occupied ? Occupied : 0) | (own ? Own : 0)
            | (own && p.Rally is not null ? Rallied : 0)));
        if (p.Owner is Side owner) w.Write7BitEncodedInt(owner.Id);
        if (known)
        {
            w.Write7BitEncodedInt(p.Station);
            w.Write7BitEncodedInt(p.Mines);
            w.Write7BitEncodedInt(p.Platforms);
            w.Write7BitEncodedInt(p.PlatformTier);
            w.Write7BitEncodedInt(p.Lab);
            w.Write7BitEncodedInt(p.RepairDock);
            w.Write7BitEncodedInt(p.IonCannon);
            w.Write7BitEncodedInt(p.StarLance);
            if (p.StarLance > 0) w.Write((float)p.LanceCharge);
            if (occupied)
            {
                w.Write7BitEncodedInt(p.Occupation!.Side.Id);
                w.Write((float)p.Occupation.Remaining);
            }
        }
        if (!own) return;
        WriteQueue(w, p.Units);
        WriteQueue(w, p.Buildings);
        w.Write7BitEncodedInt(p.Repeat.Count);
        foreach (var c in p.Repeat) w.Write((byte)c);
        if (p.Rally is int rally) w.Write7BitEncodedInt(rally);
    }

    static void WriteQueue(BinaryWriter w, List<GalaxyQueueItem> queue)
    {
        w.Write7BitEncodedInt(queue.Count);
        foreach (var q in queue)
        {
            w.Write((byte)q.Kind);
            w.Write((byte)q.Cls);
            WriteHero(w, q.Hero);
            w.Write((float)q.Remaining);
            w.Write((float)q.Total);
            w.Write((float)q.Cost);
        }
    }

    // Fleet flags.
    const int Docked = 1, Moving = 2, Fighting = 4, Routed = 8;

    /// <summary>
    /// n id, n side, ships, heroes, u8 flags, [n docked at], n from, [n next, f32 progress],
    /// [n battle], f32 condition; the viewer's own on the move, n planets of its way after the next.
    /// </summary>
    static void WriteFleet(BinaryWriter w, Fleet f, Side viewer)
    {
        w.Write7BitEncodedInt(f.Id);
        w.Write7BitEncodedInt(f.Side.Id);
        WriteShips(w, f.Ships);
        w.Write7BitEncodedInt(f.Heroes.Count);
        foreach (var h in f.Heroes) WriteHero(w, h);
        var routed = f.Side == viewer && f.To is not null;
        w.Write((byte)((f.At is not null ? Docked : 0) | (f.To is not null ? Moving : 0) | (f.BattleId is not null ? Fighting : 0) | (routed ? Routed : 0)));
        if (f.At is int at) w.Write7BitEncodedInt(at);
        w.Write7BitEncodedInt(f.From);
        if (f.To is int to)
        {
            w.Write7BitEncodedInt(to);
            w.Write((float)f.Progress);
        }
        if (f.BattleId is int battle) w.Write7BitEncodedInt(battle);
        w.Write((float)f.Condition);
        if (!routed) return;
        w.Write7BitEncodedInt(f.Path.Count);
        foreach (var id in f.Path) w.Write7BitEncodedInt(id);
    }

    // Side flags.
    const int Out = 1, Bot = 2, Viewer = 4, Grudge = 8;

    static void WriteSides(Galaxy g, BinaryWriter w, Side viewer)
    {
        w.Write7BitEncodedInt(g.Factions.Count);
        foreach (var f in g.Factions.Values)
        {
            var own = f.Id == viewer;
            var grudge = f.AI?.Grudges.GetValueOrDefault(viewer) ?? 0;
            w.Write7BitEncodedInt(f.Id.Id);
            w.Write((byte)f.Race);
            w.Write7BitEncodedInt(f.Team);
            w.Write(f.Color);
            w.Write((byte)((f.Eliminated ? Out : 0) | (f.AI is not null ? Bot : 0) | (own ? Viewer : 0) | (grudge > 0 ? Grudge : 0)));
            if (grudge > 0) w.Write((float)grudge);
            if (!own) continue;
            w.Write(f.Credits);
            w.Write7BitEncodedInt(Techs.All.Count);
            foreach (var t in Techs.All) w.Write7BitEncodedInt(f.Research[t]);
            w.Write(f.Researching is not null);
            if (f.Researching is { } job) WriteJob(w, job);
            w.Write7BitEncodedInt(f.ResearchQueue.Count);
            foreach (var j in f.ResearchQueue) WriteJob(w, j);
            w.Write7BitEncodedInt(f.HeroCooldowns.Count);
            foreach (var (hero, wait) in f.HeroCooldowns)
            {
                WriteHero(w, hero);
                w.Write((float)wait);
            }
        }
    }

    static void WriteJob(BinaryWriter w, ResearchJob job)
    {
        w.Write((byte)job.Tech);
        w.Write((float)job.Remaining);
        w.Write((float)job.Total);
        w.Write((float)job.Cost);
    }

    /// <summary>
    /// Whether the viewer hears of a battle: every one that holds the galaxy (waiting for a choice,
    /// or commanded), and the others where its team is in it, has eyes on the planet, or sees a fleet fighting.
    /// </summary>
    static bool Told(Galaxy g, GalaxyBattle b, Side viewer, HashSet<int> seen) =>
        b.Status != BattleStatus.Auto || Allies(g, b.Attacker, viewer) || Allies(g, b.Defender, viewer)
        || b.Lane is null && Known(g, viewer, g.Planets[b.PlanetId], seen)
        || g.Fleets.Values.Any(f => f.BattleId == b.Id && FleetVisible(g, viewer, f, seen));

    // Battle flags.
    const int OnLane = 1, Forces = 2;

    /// <summary>
    /// n id, n planet, u8 flags, [n lane a, b, f32 t], n attacker, n defender, u8 status, n choices
    /// (each n side, u8 status), n commanders (each n side), f32 elapsed; for the viewer's own, what
    /// each side brings, attacker first: ships, heroes, f32 strength, then its defenses (n station,
    /// platforms, mines, repair dock, ion cannon, platform tier).
    /// </summary>
    static void WriteBattle(BinaryWriter w, GalaxyBattle b, Side viewer)
    {
        var own = b.Attacker == viewer || b.Defender == viewer;
        w.Write7BitEncodedInt(b.Id);
        w.Write7BitEncodedInt(b.PlanetId);
        w.Write((byte)((b.Lane is not null ? OnLane : 0) | (own ? Forces : 0)));
        if (b.Lane is { } l)
        {
            w.Write7BitEncodedInt(l.A);
            w.Write7BitEncodedInt(l.B);
            w.Write((float)l.T);
        }
        w.Write7BitEncodedInt(b.Attacker.Id);
        w.Write7BitEncodedInt(b.Defender.Id);
        w.Write((byte)b.Status);
        w.Write7BitEncodedInt(b.Choices.Count);
        foreach (var (side, mode) in b.Choices)
        {
            w.Write7BitEncodedInt(side.Id);
            w.Write((byte)mode);
        }
        w.Write7BitEncodedInt(b.Commanders.Count);
        foreach (var side in b.Commanders) w.Write7BitEncodedInt(side.Id);
        w.Write((float)b.Elapsed);
        if (!own) return;
        foreach (var side in new[] { b.Attacker, b.Defender })
        {
            var force = ForceOf(b, side);
            WriteShips(w, force.Ships);
            w.Write7BitEncodedInt(force.Heroes.Count);
            foreach (var h in force.Heroes) WriteHero(w, h);
            w.Write((float)force.Strength);
            var d = force.Defense;
            foreach (var n in new[] { d.Station, d.Platforms, d.Mines, d.RepairDock, d.IonCannon, d.PlatformTier }) w.Write7BitEncodedInt(n);
        }
    }

    /// <summary>n classes, each: u8 class, n count. Classes with none are left out.</summary>
    static void WriteShips(BinaryWriter w, ShipCounts ships)
    {
        var classes = Ships.All.Where(c => ships.Of(c) > 0).ToList();
        w.Write7BitEncodedInt(classes.Count);
        foreach (var c in classes)
        {
            w.Write((byte)c);
            w.Write7BitEncodedInt(ships.Of(c));
        }
    }

    static void WriteHero(BinaryWriter w, HeroId h)
    {
        w.Write((byte)h.Faction);
        w.Write((byte)h.Role);
    }

    /// <summary>
    /// Whether the viewer may hear of an event, as the map would tell it: what befalls its team,
    /// planets changing hands (known everywhere), truces, random events, ruins, star lances and
    /// the end of the campaign; a battle ending where its team has eyes; truce offers it made or got.
    /// </summary>
    static bool Tells(Galaxy g, Side viewer, GalaxyEvent ev, HashSet<int> seen) => ev switch
    {
        BattleStarted s => g.Battles.Find(b => b.Id == s.BattleId) is { } b && (Allies(g, b.Attacker, viewer) || Allies(g, b.Defender, viewer)),
        BattleEnded e => Allies(g, e.Attacker, viewer) || Allies(g, e.Defender, viewer) || e.Lane is null && Known(g, viewer, g.Planets[e.PlanetId], seen),
        PlanetBuilt b => Allies(g, b.Side, viewer),
        TechResearched t => Allies(g, t.Side, viewer),
        BountyPaid b => Allies(g, b.Side, viewer),
        TruceOffered o => o.From == viewer || o.To == viewer,
        Captured or GalaxyVictory or TruceSigned or TruceEnded or IncidentStarted or IncidentEnded or RuinsExplored or LanceFired => true,
        _ => false,
    };

    static void WriteLane(BinaryWriter w, LaneSpot? lane)
    {
        w.Write(lane is not null);
        if (lane is null) return;
        w.Write7BitEncodedInt(lane.A);
        w.Write7BitEncodedInt(lane.B);
        w.Write((float)lane.T);
    }

    static void WriteTech(BinaryWriter w, TechId? tech) => w.Write(tech is TechId t ? (byte)t : byte.MaxValue);

    /// <summary>An optional side, as n side + 1 (0: none).</summary>
    static void WriteSide(BinaryWriter w, Side? side) => w.Write7BitEncodedInt(side is Side s ? s.Id + 1 : 0);

    static void WriteEvent(BinaryWriter w, GalaxyEvent ev)
    {
        switch (ev)
        {
            case BattleStarted s:
                w.Write((byte)Tag.BattleStarted);
                w.Write7BitEncodedInt(s.BattleId);
                break;
            case BattleEnded e:
                w.Write((byte)Tag.BattleEnded);
                w.Write7BitEncodedInt(e.PlanetId);
                w.Write7BitEncodedInt(e.Attacker.Id);
                w.Write7BitEncodedInt(e.Defender.Id);
                w.Write((byte)e.Result);
                WriteLane(w, e.Lane);
                break;
            case Captured c:
                w.Write((byte)Tag.Captured);
                w.Write7BitEncodedInt(c.PlanetId);
                w.Write7BitEncodedInt(c.Side.Id);
                WriteSide(w, c.From);
                break;
            case PlanetBuilt b:
                w.Write((byte)Tag.PlanetBuilt);
                w.Write7BitEncodedInt(b.PlanetId);
                w.Write7BitEncodedInt(b.Side.Id);
                w.Write(b.What);
                break;
            case TechResearched t:
                w.Write((byte)Tag.TechResearched);
                w.Write7BitEncodedInt(t.Side.Id);
                w.Write((byte)t.Tech);
                break;
            case BountyPaid b:
                w.Write((byte)Tag.BountyPaid);
                w.Write7BitEncodedInt(b.PlanetId);
                w.Write7BitEncodedInt(b.Side.Id);
                w.Write(b.Credits);
                WriteTech(w, b.Tech);
                WriteLane(w, b.Lane);
                break;
            case GalaxyVictory v:
                w.Write((byte)Tag.GalaxyVictory);
                w.Write7BitEncodedInt(v.Side.Id);
                w.Write7BitEncodedInt(v.Team);
                break;
            case TruceOffered o:
                w.Write((byte)Tag.TruceOffered);
                w.Write7BitEncodedInt(o.From.Id);
                w.Write7BitEncodedInt(o.To.Id);
                break;
            case TruceSigned s:
                w.Write((byte)Tag.TruceSigned);
                w.Write7BitEncodedInt(s.A.Id);
                w.Write7BitEncodedInt(s.B.Id);
                break;
            case TruceEnded e:
                w.Write((byte)Tag.TruceEnded);
                w.Write7BitEncodedInt(e.A.Id);
                w.Write7BitEncodedInt(e.B.Id);
                WriteSide(w, e.BrokenBy);
                break;
            case IncidentStarted i:
                w.Write((byte)Tag.IncidentStarted);
                w.Write((byte)i.Kind);
                w.Write7BitEncodedInt(i.Planet);
                w.Write7BitEncodedInt(i.Other + 1);
                WriteSide(w, i.Side);
                break;
            case IncidentEnded i:
                w.Write((byte)Tag.IncidentEnded);
                w.Write((byte)i.Kind);
                w.Write7BitEncodedInt(i.Planet);
                w.Write7BitEncodedInt(i.Other + 1);
                WriteSide(w, i.Side);
                break;
            case RuinsExplored r:
                w.Write((byte)Tag.RuinsExplored);
                w.Write7BitEncodedInt(r.PlanetId);
                w.Write7BitEncodedInt(r.Side.Id);
                w.Write(r.Credits);
                WriteTech(w, r.Tech);
                break;
            case LanceFired l:
                w.Write((byte)Tag.LanceFired);
                w.Write7BitEncodedInt(l.From);
                w.Write7BitEncodedInt(l.Target);
                w.Write7BitEncodedInt(l.Side.Id);
                break;
        }
    }
}
