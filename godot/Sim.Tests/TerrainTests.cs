// The battle's terrain (not in the web version): what each kind does, fleets going around it
// without ever getting stuck, maps that give every seat the same terrain and the same each time,
// and the terrain over the network and in replays, read back without trusting the bytes.
using System.Diagnostics;
using System.Text.Json;
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class TerrainTests(ITestOutputHelper output)
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan;

    static World NewWorld(bool fog = false)
    {
        var w = CreateWorld(4000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        w.Fog = fog;
        return w;
    }

    static TerrainPatch Piece(World w, TerrainKind kind, double x, double y, double radius, double? x2 = null, double? y2 = null, int mines = 0)
    {
        var p = new TerrainPatch(w.Terrain.Count + 1, kind, x, y, radius) { X2 = x2 ?? x, Y2 = y2 ?? y, Mines = mines, MaxMines = mines };
        w.Terrain.Add(p);
        return p;
    }

    /// <summary>A band across the way from the left of the map to its right.</summary>
    static TerrainPatch Wall(World w) => Piece(w, TerrainKind.Belt, 2000, 1300, 70, 2000, 2700);

    /// <summary>Steps until every ship has arrived or `seconds` have gone by, checking each step; the events dropped.</summary>
    static void Until(World w, IReadOnlyList<Entity> ships, double seconds, Action? each = null)
    {
        for (var i = Js.Round(seconds / Steps.Step); i > 0 && ships.Any(s => s.Order is not null); i--)
        {
            Steps.StepWorld(w);
            each?.Invoke();
            w.Events.Clear();
        }
    }

    // Each kind's effect.

    [Fact]
    public void A_ship_in_a_nebula_is_seen_only_up_close_and_still_sees_out()
    {
        var w = NewWorld(fog: true);
        var hidden = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        var eye = SpawnShip(w, Nairan, ShipClass.Scout, 1600, 1000);
        void Look()
        {
            w.VisionTimer = 0;
            Vision.UpdateVision(w, Steps.Step);
        }
        Look();
        Assert.True(Vision.IsVisible(w, Nairan, hidden));
        Piece(w, TerrainKind.Nebula, 1000, 1000, 300);
        Look();
        Assert.False(Vision.IsVisible(w, Nairan, hidden));
        Assert.True(Vision.IsVisible(w, Klaed, eye));
        eye.X = 1000 + Terrains.NebulaSight - 20;
        Look();
        Assert.True(Vision.IsVisible(w, Nairan, hidden));
    }

    [Fact]
    public void Shields_and_hulls_that_mend_come_back_slower_in_a_nebula()
    {
        var w = NewWorld();
        AddFaction(w, Faction.Nautolan, 0);
        var cls = Ships.All.First(c => Ships.Defs[c].Shield > 0 && Ships.Defs[c].ShieldRegen > 0 && !Ships.Defs[c].FromHangar);
        var inside = SpawnShip(w, Klaed, cls, 1000, 1000);
        var outside = SpawnShip(w, Klaed, cls, 3000, 3000);
        // Nautolan hulls mend themselves (their race's trait): as slowly there.
        var mending = SpawnShip(w, Faction.Nautolan, cls, 1000, 1100);
        var mended = SpawnShip(w, Faction.Nautolan, cls, 3000, 3300);
        Piece(w, TerrainKind.Nebula, 1000, 1000, 300);
        inside.Shield = outside.Shield = 0;
        var hull = mended.MaxHull / 2;
        mending.Hull = mended.Hull = hull;
        Combat.UpdateShields(w, 1);
        Assert.True(outside.Shield > 0);
        Expect.Close(outside.Shield * Terrains.NebulaRepair, inside.Shield, 6);
        Expect.Greater(mended.Hull, hull);
        Expect.Close((mended.Hull - hull) * Terrains.NebulaRepair, mending.Hull - hull, 6);
    }

    [Fact]
    public void Rocks_stop_light_shots_but_heavy_rays_and_beams_go_through()
    {
        var w = NewWorld();
        Wall(w);
        Assert.True(Terrains.Stops(w, WeaponKind.Laser, 2000, 2000));
        Assert.True(Terrains.Stops(w, WeaponKind.Torpedo, 2000, 1250));
        Assert.False(Terrains.Stops(w, WeaponKind.Ray, 2000, 2000));
        Assert.False(Terrains.Stops(w, WeaponKind.Beam, 2000, 2000));
        Assert.False(Terrains.Stops(w, WeaponKind.Laser, 2100, 2000));
        // A fight across the band: shots crash on the rocks.
        SpawnShip(w, Klaed, ShipClass.Torpedo, 1800, 2000);
        SpawnShip(w, Nairan, ShipClass.Torpedo, 2200, 2000, Math.PI);
        var crashed = 0;
        for (var i = 0; i < 600; i++)
        {
            Steps.StepWorld(w);
            crashed += w.Events.Count(e => e is Intercepted x && Math.Abs(x.X - 2000) < 120);
            w.Events.Clear();
        }
        Expect.Greater(crashed, 0);
    }

    [Fact]
    public void A_minefield_blows_under_whoever_enters_until_it_is_empty_and_pays_no_one()
    {
        var w = NewWorld();
        var field = Piece(w, TerrainKind.Minefield, 2000, 2000, 200, mines: 3);
        var s = SpawnShip(w, Klaed, ShipClass.Battlecruiser, 2000, 2000);
        s.Shield = s.MaxShield = 0;
        var away = SpawnShip(w, Klaed, ShipClass.Frigate, 3000, 3000);
        var hull = s.Hull;
        var blasts = 0;
        void Steps_(double seconds)
        {
            for (var i = Js.Round(seconds / Steps.Step); i > 0; i--)
            {
                Steps.StepWorld(w);
                blasts += w.Events.Count(e => e is MineBlast);
                w.Events.Clear();
            }
        }
        Steps_(1.05);
        Assert.Equal((2, 1), (field.Mines, blasts));
        Expect.Close(hull - Terrains.MineDamage - Terrains.MineHullShare * s.MaxHull, s.Hull, 6);
        Assert.Equal(away.MaxHull, away.Hull);
        Steps_(3);
        Assert.Equal((0, 3), (field.Mines, blasts));
        Assert.False(field.Armed);
        var after = s.Hull;
        Steps_(3);
        Assert.Equal(after, s.Hull);
        Assert.All(w.Factions.Values, f => Assert.Equal(0.0, f.Stats.Damage));
    }

    [Fact]
    public void A_strike_sets_off_the_mines_it_lands_on()
    {
        var w = NewWorld();
        var field = Piece(w, TerrainKind.Minefield, 2000, 2000, 200, mines: 12);
        Assert.False(Terrains.Clear(w, 2000, 2000, 60));
        Terrains.Blast(w, 2000, 2000, 100);
        Assert.Equal(9, field.Mines);
        Terrains.Blast(w, 2600, 2000, 100);
        Assert.Equal(9, field.Mines);
        Terrains.Blast(w, 1900, 2000, 400);
        Assert.Equal(0, field.Mines);
        // Cleared, it is open ground again.
        Assert.True(Terrains.Clear(w, 2000, 2000, 60));
    }

    [Fact]
    public void A_well_slows_and_draws_in_ships_and_bends_shots_at_their_speed()
    {
        var w = NewWorld();
        var well = Piece(w, TerrainKind.Well, 2000, 2000, 360);
        var s = SpawnShip(w, Klaed, ShipClass.Frigate, 2000, 2000);
        Expect.Close(Terrains.WellSlow, Terrains.SpeedShare(w, s), 6);
        s.X = 2180;
        Expect.Close(Terrains.WellSlow + (1 - Terrains.WellSlow) * 0.5, Terrains.SpeedShare(w, s), 6);
        s.X = 2400;
        Assert.Equal(1, Terrains.SpeedShare(w, s));
        s.X = 2200;
        Steps.Run(w, 2);
        Expect.Less(Dist(s, well), 190);
        var shot = new Projectile { Id = 999, Side = Klaed, Kind = WeaponKind.Bullet, X = 1750, Y = 1850, Vx = 600, Speed = 600, Ttl = 3, Damage = 1 };
        w.Projectiles.Add(shot);
        Terrains.Update(w, 0.1);
        Expect.Greater(shot.Vy, 0);
        Expect.Close(600, Hypot(shot.Vx, shot.Vy), 6);
    }

    [Fact]
    public void No_structure_goes_up_on_rocks_mines_or_in_a_well()
    {
        var w = NewWorld();
        Wall(w);
        Piece(w, TerrainKind.Well, 3000, 1000, 300);
        Piece(w, TerrainKind.Nebula, 1000, 1000, 300);
        Assert.False(Terrains.Clear(w, 2000, 2000, 40));
        Assert.False(Terrains.Clear(w, 2100, 2000, 40));
        Assert.False(Terrains.Clear(w, 3000, 1100, 40));
        Assert.True(Terrains.Clear(w, 1000, 1000, 40));
        Assert.True(Terrains.Clear(w, 2200, 2000, 40));
    }

    // Going around.

    [Fact]
    public void A_whole_fleet_goes_around_a_belt_none_of_it_ever_in_the_rocks()
    {
        var w = NewWorld();
        var belt = Wall(w);
        var fleet = Movement.FormationOffsets(12, 60).Select(o => SpawnShip(w, Klaed, ShipClass.Frigate, 1400 + o.X, 2000 + o.Y)).ToList();
        Movement.IssueMove(w, fleet.Select(s => s.Id), 2600, 2000);
        Until(w, fleet, 90, () =>
        {
            foreach (var s in fleet) Assert.True(belt.Distance(s.X, s.Y) >= belt.Radius, $"vaisseau {s.Id} dans la ceinture à ({s.X:F0}, {s.Y:F0})");
        });
        Assert.All(fleet, s => Assert.Null(s.Order));
        Assert.All(fleet, s => Expect.Less(Hypot(s.X - 2600, s.Y - 2000), 250));
    }

    [Theory]
    [InlineData(1400, 2000, 2000, 2000)]
    [InlineData(1400, 2000, 2095, 2000)]
    [InlineData(1400, 2000, 2000, 2780)]
    [InlineData(1400, 2000, 2000, 3400)]
    [InlineData(2000, 600, 2000, 3400)]
    [InlineData(2300, 1250, 1700, 2750)]
    public void A_point_behind_or_on_the_rocks_or_in_line_with_them_is_reached(double x, double y, double tx, double ty)
    {
        var w = NewWorld();
        Wall(w);
        var s = SpawnShip(w, Klaed, ShipClass.Battlecruiser, x, y);
        Movement.IssueMove(w, [s.Id], tx, ty);
        Until(w, [s], 120);
        Assert.Null(s.Order);
        var want = Terrains.OffRocks(w, tx, ty, s.Radius);
        Expect.Less(Hypot(s.X - want.X, s.Y - want.Y), 40);
    }

    [Fact]
    public void Ships_go_around_an_armed_minefield_and_through_a_cleared_one()
    {
        var w = NewWorld();
        var field = Piece(w, TerrainKind.Minefield, 2000, 2000, 220, mines: 14);
        var fleet = Movement.FormationOffsets(8, 60).Select(o => SpawnShip(w, Klaed, ShipClass.Frigate, 1300 + o.X, 2000 + o.Y)).ToList();
        Movement.IssueMove(w, fleet.Select(s => s.Id), 2700, 2000);
        Until(w, fleet, 90);
        Assert.All(fleet, s => Assert.Null(s.Order));
        Assert.Equal(14, field.Mines);
        // Sent into it, a ship goes: and the field empties under it.
        var s = fleet[0];
        Movement.IssueMove(w, [s.Id], 2000, 2000);
        Until(w, [s], 60);
        Assert.Null(s.Order);
        Expect.Less(field.Mines, 14);
        field.Mines = 0;
        var closest = double.PositiveInfinity;
        Movement.IssueMove(w, [s.Id], 1300, 2000);
        Until(w, [s], 60, () => closest = Math.Min(closest, Dist(s, field)));
        Expect.Less(closest, 60);
    }

    [Fact]
    public void An_AI_fleet_waits_between_waves_in_a_nebula_near_home()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 0).AI = NewAI();
        AddFaction(w, Faction.Nairan, 0);
        w.Fog = true;
        SpawnStation(w, Klaed, 800, 2000);
        SpawnStation(w, Nairan, 5200, 2000);
        Piece(w, TerrainKind.Nebula, 2000, 2000, 350);
        var fleet = new[] { SpawnShip(w, Klaed, ShipClass.Frigate, 900, 2400), SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 2400) };
        for (var i = Js.Round(40 / Steps.Step); i > 0; i--)
        {
            Steps.StepWorld(w);
            w.Events.Clear();
        }
        Assert.All(fleet, s => Assert.True(Terrains.InNebula(w, s.X, s.Y), $"({s.X:F0}, {s.Y:F0})"));
    }

    /// <summary>Lobby options of a mode, with `seats` seats.</summary>
    static SkirmishOptions Options(LobbyMode mode, int seats, MapSize size, TerrainDensity terrain, int seed, MapKind map = MapKind.Random)
    {
        var l = Lobby.Create(mode, seed);
        while (l.Seats.Count < seats && l.AddBot() is not null) { }
        l.Size = size;
        l.Map = map;
        l.Terrain = terrain;
        return l.Skirmish();
    }

    [Fact]
    public void Fleets_cross_every_generated_map_from_seat_to_seat_without_getting_stuck()
    {
        foreach (var (seats, size, seed) in new[] { (2, MapSize.Medium, 4), (3, MapSize.Large, 8), (4, MapSize.Medium, 2), (2, MapSize.Small, 13) })
        {
            var map = Scenario.CreateSkirmish(Options(LobbyMode.Skirmish, seats, size, TerrainDensity.Dense, seed));
            var spots = map.Entities.Where(e => e.Type == "station").Select(e => new Point(e.X, e.Y)).ToList();
            // The map's terrain alone, its stations left out: nothing shoots the fleets.
            var w = CreateWorld(map.Width, map.Height);
            AddFaction(w, Faction.Klaed, 0);
            w.Terrain.AddRange(map.Terrain);
            for (var i = 0; i < spots.Count; i++)
            {
                var from = spots[i];
                var to = spots[(i + 1) % spots.Count];
                var fleet = Movement.FormationOffsets(6, 60).Select(o => SpawnShip(w, Klaed, ShipClass.Frigate, from.X + o.X, from.Y + o.Y)).ToList();
                Movement.IssueMove(w, fleet.Select(s => s.Id), to.X, to.Y);
                Until(w, fleet, Hypot(to.X - from.X, to.Y - from.Y) / Ships.Defs[ShipClass.Frigate].Speed * 2.5 + 30);
                Assert.True(fleet.All(s => s.Order is null), $"{seats} places, {size}, graine {seed} : flotte bloquée de la place {i}");
                foreach (var s in fleet) w.Entities.Remove(s.Id);
            }
        }
    }

    // The map.

    static List<(TerrainKind, double, double, double, double, double, int)> Shape(World w) =>
        [.. w.Terrain.Select(p => (p.Kind, p.X, p.Y, p.X2, p.Y2, p.Radius, p.MaxMines))];

    [Fact]
    public void The_same_seed_and_settings_give_the_same_terrain_and_none_leaves_the_map_as_it_was()
    {
        foreach (var mode in new[] { LobbyMode.Skirmish, LobbyMode.Survival, LobbyMode.Zones, LobbyMode.Regicide })
        {
            var none = Scenario.CreateSkirmish(Options(mode, 3, MapSize.Medium, TerrainDensity.None, 21));
            var dense = Scenario.CreateSkirmish(Options(mode, 3, MapSize.Medium, TerrainDensity.Dense, 21));
            Assert.Empty(none.Terrain);
            output.WriteLine($"{mode} : {dense.Terrain.Count} pièces");
            Assert.Equal(Shape(dense), Shape(Scenario.CreateSkirmish(Options(mode, 3, MapSize.Medium, TerrainDensity.Dense, 21))));
            Assert.NotEqual(Shape(dense), Shape(Scenario.CreateSkirmish(Options(mode, 3, MapSize.Medium, TerrainDensity.Dense, 22))));
            // The terrain draws from its own stream: the rest of the map does not move.
            Assert.Equal(none.Fields.Select(f => (f.X, f.Y, f.Radius)), dense.Fields.Select(f => (f.X, f.Y, f.Radius)));
            Assert.Equal(none.Entities.Select(e => (e.Id, e.Type, e.X, e.Y)), dense.Entities.Select(e => (e.Id, e.Type, e.X, e.Y)));
            Assert.Equal(none.NextId, dense.NextId);
        }
    }

    [Fact]
    public void Every_mode_and_size_gets_terrain_and_a_dense_map_more()
    {
        foreach (var mode in new[] { LobbyMode.Skirmish, LobbyMode.Survival, LobbyMode.Zones, LobbyMode.Regicide, LobbyMode.Sandbox })
        {
            foreach (var size in Scenario.MapSizes.Keys)
            {
                for (var seed = 1; seed <= 3; seed++)
                {
                    var seats = mode == LobbyMode.Survival ? 1 : 2;
                    var light = Scenario.CreateSkirmish(Options(mode, seats, size, TerrainDensity.Light, seed)).Terrain;
                    var dense = Scenario.CreateSkirmish(Options(mode, seats, size, TerrainDensity.Dense, seed)).Terrain;
                    output.WriteLine($"{mode} {size} {seed} : {light.Count} / {dense.Count}");
                    Assert.True(light.Count >= 1, $"{mode} {size} {seed} : aucun terrain");
                    Assert.True(dense.Count > light.Count, $"{mode} {size} {seed} : {dense.Count} pièces denses, {light.Count} légères");
                }
            }
        }
    }

    [Fact]
    public void An_assault_has_no_terrain_and_no_beacon_lies_under_any()
    {
        var o = Options(LobbyMode.Assault, 2, MapSize.Medium, TerrainDensity.Dense, 3);
        Assert.Equal(TerrainDensity.None, o.Terrain);
        Assert.False(Lobby.Create(LobbyMode.Assault, 3).HasTerrain);
        o.Terrain = TerrainDensity.Dense;
        Assert.Empty(Scenario.CreateSkirmish(o).Terrain);
        for (var seed = 1; seed <= 4; seed++)
        {
            var w = Scenario.CreateSkirmish(Options(LobbyMode.Zones, 2, MapSize.Medium, TerrainDensity.Dense, seed));
            Assert.All(w.Mode.Beacons, b => Assert.DoesNotContain(w.Terrain, p => p.Holds(b.X, b.Y, ZoneControl.Radius)));
        }
    }

    /// <summary>A piece in a seat's frame: ahead toward the center and across, as shares of the seat's way there; its size; a band's turn from that way.</summary>
    static (TerrainKind Kind, double Ahead, double Across, double Radius, double Cos, double Sin) Local(TerrainPatch p, Point seat, Point c)
    {
        var (fx, fy) = (c.X - seat.X, c.Y - seat.Y);
        var reach = Hypot(fx, fy);
        var (ux, uy) = (fx / reach, fy / reach);
        var (mx, my) = ((p.X + p.X2) / 2 - seat.X, (p.Y + p.Y2) / 2 - seat.Y);
        var turn = p.Kind == TerrainKind.Belt ? Math.Atan2(p.Y2 - p.Y, p.X2 - p.X) - Math.Atan2(uy, ux) : 0;
        return (p.Kind, (mx * ux + my * uy) / reach, (mx * -uy + my * ux) / reach, p.Radius, Math.Cos(turn), Math.Sin(turn));
    }

    static bool Same((TerrainKind Kind, double Ahead, double Across, double Radius, double Cos, double Sin) a,
        (TerrainKind Kind, double Ahead, double Across, double Radius, double Cos, double Sin) b) =>
        a.Kind == b.Kind && Math.Abs(a.Ahead - b.Ahead) < 1e-6 && Math.Abs(a.Across - b.Across) < 1e-6 && Math.Abs(a.Radius - b.Radius) < 1e-6
        && Math.Abs(a.Cos - b.Cos) < 1e-6 && Math.Abs(a.Sin - b.Sin) < 1e-6;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Every_seat_finds_the_same_terrain_around_it(int seats)
    {
        foreach (var size in Scenario.MapSizes.Keys)
        {
            for (var seed = 1; seed <= 4; seed++)
            {
                var w = Scenario.CreateSkirmish(Options(LobbyMode.Skirmish, seats, size, TerrainDensity.Dense, seed));
                var c = new Point(w.Width / 2, w.Height / 2);
                var spots = w.Factions.Keys.Select(s => StationOf(w, s)).OfType<Entity>().Select(e => new Point(e.X, e.Y)).ToList();
                Assert.Equal(seats, spots.Count);
                // Shared by all: the middle piece, and the nebulae between the seats.
                var k = Math.Clamp(Math.Min(w.Width, w.Height) / 4000, 0.75, 1.2);
                bool Shared(TerrainPatch p) => Hypot(p.X - c.X, p.Y - c.Y) < 1e-6 || p.Kind == TerrainKind.Nebula && Math.Abs(p.Radius - 320 * k) < 1e-9;
                var shared = w.Terrain.Count(Shared);
                var frames = spots.Select(s => w.Terrain.Where(p => !Shared(p)).Select(p => Local(p, s, c)).ToList()).ToList();
                // What every seat finds in its frame; each other piece is one of those, around some seat.
                var common = frames[0].Where(a => frames.All(f => f.Exists(b => Same(a, b)))).ToList();
                var what = $"{seats} places, {size}, graine {seed} : {w.Terrain.Count} pièces, {common.Count} autour de chaque place, {shared} communes";
                Assert.True((w.Terrain.Count - shared) % seats == 0, what);
                foreach (var p in w.Terrain.Where(p => !Shared(p)))
                {
                    Assert.True(spots.Exists(s => common.Exists(a => Same(a, Local(p, s, c)))), what);
                }
            }
        }
    }

    [Fact]
    public void The_classic_map_has_its_one_terrain_mirrored_about_the_center()
    {
        var a = Scenario.CreateSkirmish(Options(LobbyMode.Skirmish, 2, MapSize.Medium, TerrainDensity.Light, 5, MapKind.Classic));
        var b = Scenario.CreateSkirmish(Options(LobbyMode.Skirmish, 2, MapSize.Medium, TerrainDensity.Light, 77, MapKind.Classic));
        Assert.NotEmpty(a.Terrain);
        Assert.Equal(Shape(a), Shape(b));
        foreach (var p in a.Terrain)
        {
            Assert.Contains(a.Terrain, q => q.Kind == p.Kind && Math.Abs(q.Radius - p.Radius) < 1e-6
                && Math.Abs(q.X + q.X2 + p.X + p.X2 - 2 * a.Width) < 1e-6 && Math.Abs(q.Y + q.Y2 + p.Y + p.Y2 - 2 * a.Height) < 1e-6);
        }
        Assert.Empty(Scenario.CreateSkirmish(Options(LobbyMode.Skirmish, 2, MapSize.Medium, TerrainDensity.None, 5, MapKind.Classic)).Terrain);
    }

    [Fact]
    public void A_battle_over_a_planet_in_a_nebula_is_fought_in_the_cloud()
    {
        static BattleSide Side(Side s) => new() { Side = s, Ships = new ShipCounts { [ShipClass.Frigate] = 3 } };
        var plain = Battle.CreatePlanetBattle(Side(Klaed), Side(Nairan), PlanetDefense.None, 3);
        var cloud = Battle.CreatePlanetBattle(Side(Klaed), Side(Nairan), PlanetDefense.None, 3, nebula: true);
        Assert.Empty(plain.Terrain);
        var p = Assert.Single(cloud.Terrain);
        Assert.Equal(TerrainKind.Nebula, p.Kind);
        Assert.Equal(plain.Entities.Select(e => (e.X, e.Y)), cloud.Entities.Select(e => (e.X, e.Y)));
    }

    // The network, the lobby and replays.

    static byte[] Whole(World w, Side viewer, SnapshotFrames? frames = null)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) Snapshot.Write(w, viewer, bw, [], null, frames);
        return ms.ToArray();
    }

    [Fact]
    public void The_terrain_comes_once_with_a_whole_snapshot_and_its_mines_with_every_one()
    {
        var host = Enumerable.Range(1, 20).Select(seed => Scenario.CreateSkirmish(Options(LobbyMode.Skirmish, 3, MapSize.Medium, TerrainDensity.Dense, seed)))
            .First(w => w.Terrain.Exists(p => p.Kind == TerrainKind.Minefield));
        var viewer = host.Factions.Keys.First();
        var sent = new SnapshotFrames();
        var read = new SnapshotFrames();
        var replica = new World();
        var first = Whole(host, viewer, sent);
        Assert.True(Snapshot.Read(new BinaryReader(new MemoryStream(first)), replica, read));
        sent.Ack(read.Latest);
        Assert.Equal(host.Terrain.Select(p => (p.Id, p.Kind, (float)p.X, (float)p.Y, (float)p.X2, (float)p.Y2, (float)p.Radius, p.Mines, p.MaxMines)),
            replica.Terrain.Select(p => (p.Id, p.Kind, (float)p.X, (float)p.Y, (float)p.X2, (float)p.Y2, (float)p.Radius, p.Mines, p.MaxMines)));
        // Then only what changes: fewer mines.
        var field = host.Terrain.First(p => p.Kind == TerrainKind.Minefield);
        field.Mines -= 2;
        Steps.StepWorld(host);
        host.Events.Clear();
        var next = Whole(host, viewer, sent);
        Expect.Less(next.Length, first.Length);
        Assert.True(Snapshot.Read(new BinaryReader(new MemoryStream(next)), replica, read));
        Assert.Equal(field.Mines, replica.Terrain.Single(p => p.Id == field.Id).Mines);
        Assert.Equal(host.Terrain.Count, replica.Terrain.Count);
    }

    [Fact]
    public void A_mine_going_off_is_told_to_whoever_sees_there()
    {
        var w = NewWorld(fog: true);
        Piece(w, TerrainKind.Minefield, 1000, 1000, 200, mines: 5);
        SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        SpawnShip(w, Nairan, ShipClass.Frigate, 3500, 3500);
        for (var i = 0; i < 65; i++) Steps.StepWorld(w);
        var blast = Assert.Single(w.Events.OfType<MineBlast>());
        World Replica(Side viewer)
        {
            var r = new World();
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Snapshot.Write(w, viewer, bw, w.Events);
            ms.Position = 0;
            Snapshot.Read(new BinaryReader(ms), r);
            return r;
        }
        Assert.Contains(Replica(Klaed).Events, e => e is MineBlast m && Math.Abs(m.X - blast.X) < 0.01);
        Assert.DoesNotContain(Replica(Nairan).Events, e => e is MineBlast);
    }

    [Fact]
    public void The_lobby_s_terrain_goes_to_its_players_and_only_known_values()
    {
        var l = Lobby.Create(LobbyMode.Zones, 4);
        Assert.Equal(TerrainDensity.Light, l.Terrain);
        var room = new LobbyRoom(l);
        room.Members.Add(new Member(Lobby.Host, "Hôte"));
        Assert.True(room.Apply(Lobby.Host, new LobbyRequest.Settings(LobbySettings.Of(room.Lobby) with { Terrain = TerrainDensity.Dense })));
        Assert.Equal(TerrainDensity.Dense, room.Lobby.Terrain);
        Assert.Equal(TerrainDensity.Dense, room.Lobby.Skirmish().Terrain);
        Assert.False(room.Apply(Lobby.Host, new LobbyRequest.Settings(LobbySettings.Of(room.Lobby) with { Terrain = (TerrainDensity)9 })));
        var ask = new Message.Ask(new LobbyRequest.Settings(LobbySettings.Of(room.Lobby)));
        var back = Assert.IsType<Message.Ask>(Protocol.Decode(Protocol.Encode(ask)));
        Assert.Equal(TerrainDensity.Dense, Assert.IsType<LobbyRequest.Settings>(back.Request).Values.Terrain);
        // Kept with the lobby on this machine, and the campaign's battles have none to set.
        var json = JsonSerializer.Serialize(room.Lobby, Lobby.Json);
        Assert.Equal(TerrainDensity.Dense, JsonSerializer.Deserialize<Lobby>(json, Lobby.Json)!.Terrain);
        var campaign = Lobby.Create(LobbyMode.Campaign, 4);
        Assert.False(campaign.HasTerrain);
    }

    static Replay Recorded(SkirmishOptions o, int steps)
    {
        var w = Scenario.CreateSkirmish(o);
        Assert.True(Replays.Record(w));
        for (var i = 0; i < steps; i++)
        {
            if (i == 300) Commands.Apply(w, o.Slots[0].Side, new Command.Move([.. w.Entities.Where(e => e.Side == o.Slots[0].Side && e.Kind == EntityKind.Ship).Select(e => e.Id)], w.Width / 2, w.Height / 2, true));
            Steps.StepWorld(w);
            w.Events.Clear();
        }
        var replay = Replays.Of(w, o.Slots[0].Side, "test", 1_700_000_000_000)!;
        using var file = new MemoryStream();
        Replays.Write(file, replay);
        file.Position = 0;
        var back = Replays.Read(file);
        Assert.Equal(Replays.Digest(w), back.Digest);
        return back;
    }

    [Fact]
    public void A_battle_on_dense_terrain_plays_again_from_its_replay_exactly()
    {
        var o = Options(LobbyMode.Skirmish, 3, MapSize.Small, TerrainDensity.Dense, 6);
        foreach (var s in o.Slots.ToList()) o.Slots[o.Slots.IndexOf(s)] = s with { AI = Difficulty.Hard };
        var replay = Recorded(o, 4800);
        Assert.Equal(TerrainDensity.Dense, replay.Options.Terrain);
        var player = new ReplayPlayer(replay);
        player.SkipTo(long.MaxValue);
        Assert.True(player.Faithful);
        Assert.NotEmpty(player.World.Terrain);
    }

    [Fact]
    public void A_replay_from_before_the_terrain_still_reads_without_any()
    {
        var o = Options(LobbyMode.Skirmish, 2, MapSize.Small, TerrainDensity.None, 6);
        var w = Scenario.CreateSkirmish(o);
        Replays.Record(w);
        var replay = Replays.Of(w, o.Slots[0].Side, "test", 1_700_000_000_000)!;
        using var file = new MemoryStream();
        Replays.Write(file, replay);
        var bytes = file.ToArray();
        // Format 1: no personality byte closing each seat's 16 (format 3), after the header and the
        // options' first fields; at the options' end, before the count of commands (none), neither
        // the terrain byte nor the waves' strength (format 4).
        var seats = 4 + 4 + 4 + 1 + "test".Length + 8 + 8 + 4 + 4 + 4 + 1 + 1 + 4 + 4 + 4;
        var dropped = Enumerable.Range(0, o.Slots.Count).Select(i => seats + 16 * i + 15).Concat(Enumerable.Range(bytes.Length - 9, 5)).ToHashSet();
        var old = bytes.Where((_, i) => !dropped.Contains(i)).ToArray();
        BitConverter.GetBytes(1).CopyTo(old, 4);
        var back = Replays.Read(new MemoryStream(old));
        Assert.Equal(TerrainDensity.None, back.Options.Terrain);
        Assert.Equal((o.Seed, o.Size), (back.Options.Seed, back.Options.Size));
        // Its AIs all played Balanced.
        Assert.Equal(o.Slots.Select(s => s with { Personality = Personality.Balanced }), back.Options.Slots);
        // An unknown terrain is refused.
        bytes[^9] = 7;
        Assert.Throws<InvalidDataException>(() => Replays.Read(new MemoryStream(bytes)));
    }

    // Robustness.

    /// <summary>A whole snapshot of a world with one piece of each kind, its band's radius a mark to find in the bytes.</summary>
    static (byte[] Bytes, int Radius) Marked()
    {
        var w = NewWorld();
        SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        Piece(w, TerrainKind.Belt, 2000, 1300, 70.125, 2000, 2700);
        Piece(w, TerrainKind.Minefield, 1000, 3000, 200, mines: 12);
        Piece(w, TerrainKind.Nebula, 3000, 1000, 300);
        Piece(w, TerrainKind.Well, 3000, 3000, 300);
        var bytes = Whole(w, Klaed);
        var mark = BitConverter.GetBytes(70.125f);
        var at = bytes.AsSpan().IndexOf(mark);
        Assert.True(at > 0);
        return (bytes, at);
    }

    static void Refused(byte[] bytes) =>
        Assert.ThrowsAny<Exception>(() => Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), new World()));

    [Fact]
    public void Forged_terrain_is_refused_a_count_a_kind_a_number_at_a_time()
    {
        var (bytes, radius) = Marked();
        Assert.True(Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), new World()));
        // The band's record: id, kind, x, y, x2, y2, then the radius; the count of pieces before it.
        var kind = radius - 17;
        var count = kind - 2;
        Assert.Equal((byte)TerrainKind.Belt, bytes[kind]);
        Assert.Equal(4, bytes[count]);
        var forged = (byte[])bytes.Clone();
        forged[kind] = 9;
        Assert.IsType<InvalidDataException>(Record.Exception(() => Snapshot.Read(new BinaryReader(new MemoryStream(forged)), new World())));
        forged = (byte[])bytes.Clone();
        BitConverter.GetBytes(float.NaN).CopyTo(forged, radius);
        Assert.IsType<InvalidDataException>(Record.Exception(() => Snapshot.Read(new BinaryReader(new MemoryStream(forged)), new World())));
        forged = (byte[])bytes.Clone();
        BitConverter.GetBytes(-5f).CopyTo(forged, radius);
        Assert.IsType<InvalidDataException>(Record.Exception(() => Snapshot.Read(new BinaryReader(new MemoryStream(forged)), new World())));
        // A huge count of pieces: refused at once, nothing allocated for it.
        var huge = bytes[..count].Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }).Concat(bytes[(count + 1)..]).ToArray();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        Refused(huge);
        Expect.Less(GC.GetAllocatedBytesForCurrentThread() - allocated, 1 << 20);
        var replica = new World();
        Assert.True(Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), replica));
        Assert.Equal(4, replica.Terrain.Count);
    }

    [Fact]
    public void Broken_terrain_bytes_are_refused_and_never_bring_the_reader_down()
    {
        var (bytes, radius) = Marked();
        var rng = new Random(4242);
        for (var i = 0; i < 600; i++)
        {
            var b = bytes.ToList();
            // Around the terrain: from the count of pieces to the mine counts after them.
            var at = Math.Clamp(radius - 20 + rng.Next(110), 0, b.Count - 1);
            switch (i % 4)
            {
                case 0:
                    b = b.Take(at).ToList();
                    break;
                case 1:
                    b[at] = (byte)rng.Next(256);
                    break;
                case 2:
                    b.InsertRange(at, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
                    break;
                default:
                    b.InsertRange(at, Enumerable.Range(0, rng.Next(1, 9)).Select(_ => (byte)rng.Next(256)));
                    break;
            }
            var clock = Stopwatch.StartNew();
            var e = Record.Exception(() => Snapshot.Read(new BinaryReader(new MemoryStream([.. b])), new World()));
            Assert.True(e is null or InvalidDataException or EndOfStreamException, $"{e?.GetType().Name} : {e?.Message}");
            Expect.Less(clock.Elapsed.TotalSeconds, 1);
        }
    }
}
