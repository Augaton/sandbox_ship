// The reserve (not in the web version): large hulls wait in reserve once built and are called in
// by hyperspace, round their side's forces, never by a foe's structure; the AI calls its own in.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class ReserveTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan;

    /// <summary>Two stations far apart on a wide map, the Kla'ed's at level 4, credits to spare.</summary>
    static World NewWorld()
    {
        var w = CreateWorld(6000, 3000);
        AddFaction(w, Faction.Klaed, 200000);
        AddFaction(w, Faction.Nairan, 0);
        SpawnStation(w, Klaed, 800, 1500, 4);
        SpawnStation(w, Nairan, 5200, 1500);
        return w;
    }

    /// <summary>Builds one of this class at once: its time cut short, a step to deliver it.</summary>
    static void Build(World w, ShipClass cls)
    {
        Assert.True(Structures.EnqueueShip(w, Klaed, cls).Ok);
        w.Factions[Klaed].Queue[^1].Remaining = 0.001;
        Steps.StepWorld(w);
    }

    static int Count(World w, ShipClass cls) => w.Entities.Count(e => e.Kind == EntityKind.Ship && e.Side == Klaed && e.Cls == cls);

    [Fact]
    public void Every_ship_goes_to_the_reserve_and_a_squadron_comes_in_together()
    {
        var w = NewWorld();
        Build(w, ShipClass.Battlecruiser);
        Assert.Equal([new ReserveShip(ShipClass.Battlecruiser)], w.Factions[Klaed].Reserve);
        Assert.Equal(0, Count(w, ShipClass.Battlecruiser));
        Build(w, ShipClass.Frigate);
        Assert.Equal(0, Count(w, ShipClass.Frigate));
        Assert.Equal(2, w.Factions[Klaed].Reserve.Count);
        // A squadron is four in reserve, and one call brings all four, side by side.
        Build(w, ShipClass.Fighter);
        var fighter = new ReserveShip(ShipClass.Fighter);
        Assert.Equal(4, w.Factions[Klaed].Reserve.Count(s => s == fighter));
        Assert.True(Reserves.Deploy(w, Klaed, fighter, 1200, 1500).Ok);
        Assert.Equal(0, w.Factions[Klaed].Reserve.Count(s => s == fighter));
        var jumps = w.Jumps.Where(j => j.Cls == ShipClass.Fighter).ToList();
        Assert.Equal(4, jumps.Count);
        Assert.All(jumps, j => Assert.Equal(Reserves.SmallJumpTime, j.Total));
        Assert.True(jumps.Zip(jumps.Skip(1)).All(p => Math.Abs(Hypot(p.First.X - p.Second.X, p.First.Y - p.Second.Y) - Reserves.SquadGap) < 1e-6));
        Steps.Run(w, Reserves.SmallJumpTime + 0.1);
        Assert.Equal(4, Count(w, ShipClass.Fighter));
        Assert.Equal(Reserves.MediumJumpTime, Reserves.JumpTimeOf(ShipClass.Frigate));
    }

    [Fact]
    public void A_ship_is_called_in_round_its_sides_forces_never_by_a_foes_structure()
    {
        var w = NewWorld();
        Build(w, ShipClass.Battlecruiser);
        var ship = new ReserveShip(ShipClass.Battlecruiser);
        Assert.Equal("Trop loin de tes forces", Reserves.CanDeploy(w, Klaed, ship, 3000, 1500).Reason);
        Assert.Equal("Hors de la carte", Reserves.CanDeploy(w, Klaed, ship, 10, 1500).Reason);
        Assert.Equal("Pas en réserve", Reserves.CanDeploy(w, Klaed, new ReserveShip(ShipClass.Titan), 1200, 1500).Reason);
        // A warship of its own out there carries the zone with it, but not up to a foe's station.
        SpawnShip(w, Klaed, ShipClass.Frigate, 4300, 1500);
        Assert.True(Reserves.CanDeploy(w, Klaed, ship, 4000, 1500).Ok);
        Assert.Equal("Trop près d'un bâtiment ennemi", Reserves.CanDeploy(w, Klaed, ship, 4500, 1500).Reason);
        Assert.True(Reserves.CanDeploy(w, Klaed, ship, 1300, 1500).Ok);
        // The Nairan cannot call in a Kla'ed ship, nor anything they do not have.
        Assert.False(Reserves.CanDeploy(w, Nairan, ship, 5000, 1500).Ok);
    }

    [Fact]
    public void A_jump_takes_its_time_and_the_ship_comes_out_on_its_point_facing_away_from_home()
    {
        var w = NewWorld();
        Build(w, ShipClass.Battlecruiser);
        var r = Commands.Apply(w, Klaed, new Command.Deploy(ShipClass.Battlecruiser, null, 1500, 1200));
        Assert.True(r.Ok, r.Reason);
        Assert.Empty(w.Factions[Klaed].Reserve);
        var jump = Assert.Single(w.Jumps);
        Assert.Equal(Reserves.JumpTime, jump.Total);
        // A second jump onto the same point is refused while the first is under way.
        Build(w, ShipClass.Battlecruiser);
        Assert.Equal("Point d'arrivée déjà pris", Reserves.CanDeploy(w, Klaed, new ReserveShip(ShipClass.Battlecruiser), 1510, 1200).Reason);
        Steps.Run(w, Reserves.JumpTime - 0.1);
        Assert.Equal(0, Count(w, ShipClass.Battlecruiser));
        w.Events.Clear();
        Steps.Run(w, 0.2);
        var s = Assert.Single(w.Entities, e => e.Kind == EntityKind.Ship && e.Cls == ShipClass.Battlecruiser);
        Assert.Contains(w.Events, e => e is Delivered d && d.Id == s.Id);
        Assert.Empty(w.Jumps);
        Expect.Close(1500, s.X, 0);
        Expect.Close(1200, s.Y, 0);
        Expect.Close(Math.Atan2(1200 - 1500, 1500 - 800), s.Angle, 2);
        // Capitals take longer.
        Assert.Equal(Reserves.CapitalJumpTime, Reserves.JumpTimeOf(ShipClass.Leviathan));
    }

    [Fact]
    public void A_hero_goes_through_the_reserve_and_comes_out_a_hero_even_the_first_one()
    {
        // The Kla'ed admiral is HeroId's default value: it must not pass for no hero.
        var w = NewWorld();
        var admiral = new HeroId(Faction.Klaed, HeroRole.Admiral);
        Assert.Equal(default, admiral);
        Assert.True(Structures.EnqueueHero(w, Klaed, admiral).Ok);
        w.Factions[Klaed].Queue[^1].Remaining = 0.001;
        Steps.StepWorld(w);
        Assert.Equal([new ReserveShip(Heroes.Defs[admiral].Base, admiral)], w.Factions[Klaed].Reserve);
        Assert.False(Structures.CanBuildHero(w, Klaed, admiral).Ok);
        Assert.True(Commands.Apply(w, Klaed, new Command.Deploy(Heroes.Defs[admiral].Base, admiral, 1400, 1500)).Ok);
        Steps.Run(w, Reserves.JumpTime + 0.1);
        Assert.Single(w.Entities, e => e.Hero == admiral && e.Side == Klaed);
    }

    [Fact]
    public void The_reserve_counts_against_the_limit_of_a_class()
    {
        var w = NewWorld();
        Build(w, ShipClass.Leviathan);
        Assert.Equal(Ships.Defs[ShipClass.Leviathan].LimitReason, Structures.CanBuildShip(w, Klaed, ShipClass.Leviathan).Reason);
        Assert.True(Commands.Apply(w, Klaed, new Command.Deploy(ShipClass.Leviathan, null, 1400, 1500)).Ok);
        Assert.Equal(Ships.Defs[ShipClass.Leviathan].LimitReason, Structures.CanBuildShip(w, Klaed, ShipClass.Leviathan).Reason);
    }

    [Fact]
    public void A_ship_out_of_hyperspace_throws_craft_aside()
    {
        var w = NewWorld();
        Build(w, ShipClass.Dreadnought);
        var craft = SpawnShip(w, Nairan, ShipClass.Fighter, 1520, 1500);
        Movement.IssueStop(w, [craft.Id]);
        Assert.True(Commands.Apply(w, Klaed, new Command.Deploy(ShipClass.Dreadnought, null, 1500, 1500)).Ok);
        Steps.Run(w, Reserves.JumpTime + 0.05);
        var start = craft.X;
        Expect.Greater(craft.KnockX, 0);
        Steps.Run(w, 0.5);
        Expect.Greater(craft.X, start + 20);
        Steps.Run(w, 2);
        Assert.Equal(0, craft.KnockX);
    }

    [Fact]
    public void The_ai_calls_its_reserve_in_where_its_fleet_gathers()
    {
        var w = NewWorld();
        w.Factions[Klaed].AI = new AIState(Difficulty.Normal);
        Build(w, ShipClass.Battlecruiser);
        Steps.Run(w, 3);
        Assert.Empty(w.Factions[Klaed].Reserve);
        var f = w.Factions[Klaed];
        var arrived = w.Jumps.Count > 0 ? (w.Jumps[0].X, w.Jumps[0].Y) : w.Entities.Where(e => e.Cls == ShipClass.Battlecruiser && e.Kind == EntityKind.Ship).Select(e => (e.X, e.Y)).Single();
        Expect.Less(Hypot(arrived.Item1 - f.RallyX, arrived.Item2 - f.RallyY), 300);
    }

    [Fact]
    public void A_snapshot_carries_the_viewers_reserve_and_every_jump()
    {
        var w = NewWorld();
        Build(w, ShipClass.Battlecruiser);
        Build(w, ShipClass.Carrier);
        Assert.True(Commands.Apply(w, Klaed, new Command.Deploy(ShipClass.Carrier, null, 1400, 1500)).Ok);
        foreach (var viewer in new[] { Klaed, Nairan })
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms)) Snapshot.Write(w, viewer, bw);
            var bytes = ms.ToArray();
            var replica = new World();
            Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), replica);
            // The reserve is its own side's business; a jump is seen by all.
            var jump = Assert.Single(replica.Jumps);
            Assert.Equal((Klaed, ShipClass.Carrier, 1400f, 1500f), (jump.Side, jump.Cls, (float)jump.X, (float)jump.Y));
            if (viewer == Klaed) Assert.Equal([new ReserveShip(ShipClass.Battlecruiser)], replica.Factions[Klaed].Reserve);
            Assert.Empty(replica.Factions[viewer == Klaed ? Nairan : Klaed].Reserve);
            using var back = new MemoryStream();
            using (var bw = new BinaryWriter(back)) Snapshot.Write(replica, viewer, bw);
            Assert.Equal(bytes, back.ToArray());
        }
    }
}
