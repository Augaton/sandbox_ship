// Commands (not in the web version): what the screens send instead of calling the simulation.
// Applied at once they do what the old calls did; kept for the next step, as a host keeps a
// remote player's, they still do, when they arrive at the same step boundaries. An issuer only
// orders what is its own, and nothing malformed gets through.
using System.Text;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class CommandsTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan;

    /// <summary>A small skirmish, Kla'ed (no AI) with a few ships against a Nairan AI, rich enough to build; a Nairan bulwark comes in sight.</summary>
    static World Battle()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = true, Seed = 5, Size = MapSize.Small, Credits = 20000,
        });
        var station = StationOf(w, Klaed)!;
        ShipClass[] fleet = [ShipClass.Frigate, ShipClass.Frigate, ShipClass.Battlecruiser, ShipClass.Support, ShipClass.Bomber, ShipClass.Scout];
        for (var i = 0; i < fleet.Length; i++) SpawnShip(w, Klaed, fleet[i], station.X + 250 + i * 45, station.Y + 60);
        SpawnShip(w, Nairan, ShipClass.Bulwark, station.X + 900, station.Y + 300);
        return w;
    }

    static int[] Ships(World w, Side side) => [.. w.Entities.Where(e => e.Side == side && e.Kind == EntityKind.Ship && !e.Launched).Select(e => e.Id)];

    static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);

    /// <summary>Everything the steps and the commands touch, to the bit.</summary>
    static string Digest(World w)
    {
        var sb = new StringBuilder($"{Bits(w.Time)} {w.Seed} {w.NextId} {w.Projectiles.Count} {w.Winner}");
        foreach (var e in w.Entities)
        {
            sb.Append($"|{e.Id} {e.Type} {e.Side} {Bits(e.X)} {Bits(e.Y)} {Bits(e.Angle)} {Bits(e.Speed)} {Bits(e.Hull)} {Bits(e.Shield)} {e.TargetId}");
            sb.Append($" {e.Order?.Type} {Bits(e.Order?.X ?? 0)} {e.Order?.TargetId} {Bits(e.GuardX)} {e.Pilot?.Thrust} {e.Pilot?.Firing} {Bits(e.Pilot?.AimX ?? 0)}");
            foreach (var a in e.Abilities) sb.Append($" {a.Id}:{Bits(a.Cooldown)}");
        }
        foreach (var f in w.Factions.Values)
        {
            sb.Append($"|{f.Id} {Bits(f.Credits)} {f.Queue.Count} {f.BuildQueue.Count} {string.Join(",", f.Repeat)} {ResearchQueue.Jobs(f).Count} {Bits(f.RallyX)} {f.AutoAbilities}");
        }
        return sb.ToString();
    }

    /// <summary>What the screens did before commands, call by call; they only named entities that were there.</summary>
    static void Direct(World w, Side side, Command c)
    {
        var f = w.Factions[side];
        switch (c)
        {
            case Command.Move m: Movement.IssueMove(w, m.Ships, m.X, m.Y, m.AttackMove); break;
            case Command.Attack a: Movement.IssueAttack(w, a.Ships, a.Target); break;
            case Command.Stop s: Movement.IssueStop(w, s.Ships); break;
            case Command.UseAbility u: AbilitySystem.UseAbility(w, w.Entities.Get(u.Caster)!, u.Ability, u.Aim); break;
            case Command.EnqueueShip q:
                var first = Structures.EnqueueShip(w, side, q.Cls);
                for (var i = 1; first.Ok && i < q.Count; i++)
                {
                    if (!Structures.EnqueueShip(w, side, q.Cls).Ok) break;
                }
                break;
            case Command.UpgradeStation: Structures.EnqueueUpgrade(w, side); break;
            case Command.StartResearch r: Structures.StartResearch(w, side, r.Tech); break;
            case Command.PlaceStructure p: Structures.PlaceStructure(w, side, p.Type, p.X, p.Y); break;
            case Command.CancelQueued q: Structures.CancelQueued(w, side, q.Index, q.Queue); break;
            case Command.SetRally r:
                f.RallyX = r.X;
                f.RallyY = r.Y;
                break;
            case Command.SetLoop l:
                if (f.Repeat.Contains(l.Cls)) f.Repeat.Remove(l.Cls);
                else f.Repeat.Add(l.Cls);
                break;
            case Command.SetAutoAbilities a: f.AutoAbilities = a.On; break;
            case Command.StartPilot p:
                var s0 = w.Entities.Get(p.Ship)!;
                s0.Order = null;
                s0.Pilot = new Pilot { AimX = s0.X, AimY = s0.Y };
                break;
            case Command.PilotInput p:
                var pilot = w.Entities.Get(p.Ship)!.Pilot!;
                (pilot.Thrust, pilot.Turn, pilot.AimX, pilot.AimY, pilot.Firing) = (p.Thrust, p.Turn, p.AimX, p.AimY, p.Firing);
                break;
            case Command.StopPilot p:
                var s1 = w.Entities.Get(p.Ship)!;
                s1.Pilot = null;
                (s1.GuardX, s1.GuardY) = (s1.X, s1.Y);
                break;
            default: throw new ArgumentException(c.ToString());
        }
    }

    /// <summary>
    /// The player's orders through a battle, by step: computed from the world as it stands, as a
    /// screen would, and only at what the player sees. A frigate is flown by hand a while.
    /// </summary>
    static List<Command> Script(World w, int step)
    {
        var station = StationOf(w, Klaed)!;
        var ships = Ships(w, Klaed);
        var foe = StationOf(w, Nairan)!;
        var seen = w.Entities.FirstOrDefault(e => e.Side == Nairan && e.Kind == EntityKind.Ship && Vision.IsVisible(w, Klaed, e));
        var support = w.Entities.FirstOrDefault(e => e.Side == Klaed && e.Type == "support");
        var flown = w.Entities.FirstOrDefault(e => e.Side == Klaed && e.Type == "frigate");
        if (flown is null && step is 400 or 401 or 700 or 900) return [];
        return step switch
        {
            10 => [new Command.Move(ships, station.X + 900, station.Y - 300), new Command.EnqueueShip(ShipClass.Frigate, 5), new Command.SetLoop(ShipClass.Fighter, true)],
            20 when seen is not null => [new Command.Attack(ships, seen.Id)],
            // Two at the same boundary, the second on what the first did.
            11 => [new Command.EnqueueShip(ShipClass.Torpedo), new Command.CancelQueued(1, QueueSide.Units)],
            60 => [new Command.StartResearch(TechId.Weapons), new Command.StartResearch(TechId.Engines), new Command.SetRally(station.X + 500, station.Y)],
            90 => [new Command.PlaceStructure(BuildableType.Platform, station.X + 300, station.Y - 300), new Command.UpgradeStation()],
            200 when support is not null => [new Command.UseAbility(support.Id, AbilityId.Repair), new Command.Move(ships, foe.X, foe.Y, true)],
            400 => [new Command.StartPilot(flown!.Id), new Command.SetAutoAbilities(false)],
            401 => [new Command.PilotInput(flown!.Id, 1, -1, station.X + 2000, station.Y, true)],
            700 => [new Command.PilotInput(flown!.Id, 0, 1, station.X, station.Y - 2000, false)],
            900 => [new Command.StopPilot(flown!.Id), new Command.SetLoop(ShipClass.Fighter, false), .. seen is null ? [] : new Command[] { new Command.Attack(ships, seen.Id) }],
            1500 => [new Command.Stop(ships[2..])],
            _ => [],
        };
    }

    [Fact]
    public void Commands_applied_at_once_or_at_the_next_step_do_what_the_direct_calls_did()
    {
        var direct = Battle();
        var local = Battle();
        var delayed = Battle();
        var idle = Battle();
        var now = new LocalSink<Command>(c => Commands.Apply(local, Klaed, c));
        var later = new DelayedSink<Command>(c => Commands.Apply(delayed, Klaed, c));
        var sent = new List<Command>();
        for (var step = 0; step < 2400; step++)
        {
            foreach (var c in Script(direct, step))
            {
                Direct(direct, Klaed, c);
                now.Send(c);
                Assert.True(later.Send(c).Ok);
                sent.Add(c);
            }
            // The host's turn: what came since the last step goes in first.
            later.Flush();
            Assert.Equal(0, later.Pending);
            Steps.StepWorld(direct);
            Steps.StepWorld(local);
            Steps.StepWorld(delayed);
            Steps.StepWorld(idle);
            if (step % 300 == 0) Assert.Equal(Digest(direct), Digest(local));
        }
        Assert.Equal(Digest(direct), Digest(local));
        Assert.Equal(Digest(direct), Digest(delayed));
        // The whole script went out, and it did change the battle.
        Assert.Equal(21, sent.Count);
        Assert.Equal(2, sent.Count(c => c is Command.Attack));
        Assert.NotEqual(Digest(idle), Digest(direct));
        var klaed = direct.Factions[Klaed];
        Assert.True(ResearchQueue.Jobs(klaed).Count > 0 || klaed.Research[TechId.Weapons] > 0);
        Assert.False(klaed.AutoAbilities);
        Assert.Empty(klaed.Repeat);
    }

    [Fact]
    public void An_issuer_only_orders_its_own_and_targets_what_its_team_sees()
    {
        var w = Battle();
        var mine = Ships(w, Klaed);
        var station = StationOf(w, Nairan)!;
        var theirs = SpawnShip(w, Nairan, ShipClass.Frigate, station.X - 300, station.Y);
        Steps.Run(w, 1);
        // A foe's ship among the ships ordered: the whole order is refused.
        var before = theirs.Order;
        Assert.False(Commands.Apply(w, Klaed, new Command.Move([mine[0], theirs.Id], 100, 100)).Ok);
        Assert.Null(w.Entities.Get(mine[0])!.Order);
        Assert.Equal(before, theirs.Order);
        Assert.False(Commands.Apply(w, Klaed, new Command.Stop([theirs.Id])).Ok);
        Assert.False(Commands.Apply(w, Klaed, new Command.StartPilot(theirs.Id)).Ok);
        Assert.False(Commands.Apply(w, Klaed, new Command.UseAbility(theirs.Id, AbilityId.Sprint)).Ok);
        Assert.False(Commands.Apply(w, Klaed, new Command.UpgradeStructure(station.Id)).Ok);
        // The foe's station is far off in the fog: it cannot be attacked by sight it does not have.
        Assert.False(Vision.IsVisible(w, Klaed, station));
        Assert.Equal("Cible hors de vue", Commands.Apply(w, Klaed, new Command.Attack(mine, station.Id)).Reason);
        Assert.All(mine, id => Assert.Null(w.Entities.Get(id)!.Order));
        // Ids of ships gone since pass: the order skips them.
        Assert.True(Commands.Apply(w, Klaed, new Command.Move([.. mine, 99999], 800, 800)).Ok);
        Assert.All(mine, id => Assert.NotNull(w.Entities.Get(id)!.Order));
        // A ship nobody flies takes no controls.
        Assert.False(Commands.Apply(w, Klaed, new Command.PilotInput(mine[0], 1, 0, 0, 0, true)).Ok);
    }

    [Fact]
    public void Malformed_commands_and_sandbox_tools_outside_the_sandbox_are_refused()
    {
        var w = Battle();
        var mine = Ships(w, Klaed);
        var credits = w.Factions[Klaed].Credits;
        Command[] refused =
        [
            new Command.Move(mine, double.NaN, 0), new Command.SetRally(double.PositiveInfinity, 0), new Command.Move(null!, 0, 0),
            new Command.EnqueueShip((ShipClass)99), new Command.EnqueueShip(ShipClass.Frigate, 0), new Command.StartResearch((TechId)77),
            new Command.EnqueueHero(new HeroId(Faction.Errant, HeroRole.Admiral)), new Command.PlaceStructure((BuildableType)50, 10, 10),
            new Command.CancelQueued(0, (QueueSide)9), new Command.UseAbility(mine[0], (AbilityId)200),
            new Command.SetAI(Nairan, (Difficulty)8),
            // The sandbox's tools are closed by default.
            new Command.GrantCredits(5000), new Command.Spawn(Faction.Klaed, "titan", 500, 500, 0), new Command.SetFog(false),
            new Command.SetAI(Nairan, null), new Command.Destroy(mine),
        ];
        foreach (var c in refused) Assert.False(Commands.Apply(w, Klaed, c).Ok, c.ToString());
        Assert.Equal(credits, w.Factions[Klaed].Credits);
        Assert.True(w.Fog);
        Assert.NotNull(w.Factions[Nairan].AI);
        Assert.All(mine, id => Assert.True(w.Entities.Get(id)!.Hull > 0));
        // Open, they work; an Errant hero, which does not exist, is refused rather than thrown on.
        w.SandboxTools = true;
        Assert.True(Commands.Apply(w, Klaed, new Command.GrantCredits(5000)).Ok);
        Assert.Equal(credits + 5000, w.Factions[Klaed].Credits);
        Assert.True(Commands.Apply(w, Klaed, new Command.Spawn(Faction.Errant, "titan", 900, 900, 1)).Ok);
        Assert.Equal(Faction.Errant, w.Entities.Last(e => e.Kind == EntityKind.Ship).Race);
        var count = w.Entities.Count;
        Assert.False(Commands.Apply(w, Klaed, new Command.Spawn(Faction.Errant, "admiral", 900, 900, 1)).Ok);
        // A number is no hero role, whatever Enum.TryParse thinks: nothing comes of it.
        Assert.True(Commands.Apply(w, Klaed, new Command.Spawn(Faction.Klaed, "7", 900, 900, 1)).Ok);
        Assert.Equal(count, w.Entities.Count);
        Assert.True(Commands.Apply(w, Klaed, new Command.SetAI(Nairan, null)).Ok);
        Assert.Null(w.Factions[Nairan].AI);
        Assert.True(Commands.Apply(w, Klaed, new Command.Destroy([mine[0]])).Ok);
        Steps.StepWorld(w);
        Assert.False(w.Entities.Has(mine[0]));
    }

    /// <summary>The classic galaxy: Kla'ed (player) against a Nairan AI.</summary>
    static Galaxy Campaign() => Galaxies.CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, seed: 11);

    static string Digest(Galaxy g)
    {
        var sb = new StringBuilder($"{Bits(g.Time)} {g.Seed} {g.NextId} {g.Battles.Count} {g.Winner}");
        foreach (var f in g.Fleets.Values)
        {
            sb.Append($"|{f.Id} {f.Side} {f.At} {f.From} {f.To} {string.Join(",", f.Path)} {Bits(f.Progress)} {Sim.Battle.CountShips(f.Ships)} {f.Heroes.Count}");
        }
        foreach (var p in g.Planets) sb.Append($"|{p.Owner} {p.Station} {p.Units.Count} {p.Buildings.Count} {string.Join(",", p.Repeat)} {p.Rally}");
        foreach (var f in g.Factions.Values) sb.Append($"|{f.Id} {Bits(f.Credits)} {ResearchQueue.Jobs(f).Count}");
        return sb.ToString();
    }

    static void Direct(Galaxy g, Side side, GalaxyCommand c)
    {
        switch (c)
        {
            case GalaxyCommand.SendFleet s: Galaxies.SendFleet(g, s.Fleet, s.Dest, s.Ships, s.Heroes); break;
            case GalaxyCommand.RedirectFleet r: Galaxies.RedirectFleet(g, r.Fleet, r.Dest); break;
            case GalaxyCommand.StopFleet s: Galaxies.StopFleet(g, s.Fleet); break;
            case GalaxyCommand.EnqueueBuild b: Galaxies.EnqueueBuild(g, side, b.Planet, b.Build); break;
            case GalaxyCommand.CancelBuild b: Galaxies.CancelBuild(g, b.Planet, b.Index, b.Queue); break;
            case GalaxyCommand.SetLoop l: Galaxies.SetLoop(g, side, l.Planet, l.Cls, l.On); break;
            case GalaxyCommand.SetRally r: Galaxies.SetRally(g, side, r.Planet, r.Target); break;
            case GalaxyCommand.StartTech t: Galaxies.StartTech(g, side, t.Tech); break;
            default: throw new ArgumentException(c.ToString());
        }
    }

    static List<GalaxyCommand> Script(Galaxy g, int step)
    {
        var home = GalaxyMap.Capitals[0];
        var next = Galaxies.Neighbours(g, home);
        var fleet = Galaxies.FleetsAt(g, home, Klaed).FirstOrDefault();
        var moving = g.Fleets.Values.FirstOrDefault(f => f.Side == Klaed && f.At is null);
        return step switch
        {
            // One ship of the home fleet leaves: the fleet splits.
            5 => [new GalaxyCommand.SendFleet(fleet!.Id, next[0], new ShipCounts { [fleet.Ships.First(k => k.Value > 0).Key] = 1 }),
                new GalaxyCommand.EnqueueBuild(home, GalaxyBuild.Ship(ShipClass.Frigate))],
            6 => [new GalaxyCommand.EnqueueBuild(home, GalaxyBuild.Ship(ShipClass.Scout)), new GalaxyCommand.CancelBuild(home, 1, QueueSide.Units)],
            30 => [new GalaxyCommand.SetLoop(home, ShipClass.Fighter, true), new GalaxyCommand.SetRally(home, next[1])],
            60 => moving is null ? [] : [new GalaxyCommand.RedirectFleet(moving.Id, next[1])],
            61 => moving is null ? [] : [new GalaxyCommand.StopFleet(moving.Id)],
            120 => [new GalaxyCommand.EnqueueBuild(home, GalaxyBuild.Of(GalaxyBuildKind.Mine)), new GalaxyCommand.StartTech(TechId.Weapons)],
            _ => [],
        };
    }

    [Fact]
    public void Galaxy_commands_applied_at_once_or_at_the_next_step_do_what_the_direct_calls_did()
    {
        var direct = Campaign();
        var local = Campaign();
        var delayed = Campaign();
        var now = new LocalSink<GalaxyCommand>(c => Commands.Apply(local, Klaed, c));
        var later = new DelayedSink<GalaxyCommand>(c => Commands.Apply(delayed, Klaed, c));
        for (var step = 0; step < 900; step++)
        {
            foreach (var c in Script(direct, step))
            {
                Direct(direct, Klaed, c);
                now.Send(c);
                later.Send(c);
            }
            later.Flush();
            Galaxies.StepGalaxy(direct);
            Galaxies.StepGalaxy(local);
            Galaxies.StepGalaxy(delayed);
        }
        Assert.Equal(Digest(direct), Digest(local));
        Assert.Equal(Digest(direct), Digest(delayed));
        Assert.Contains(direct.Fleets.Values, f => f.Side == Klaed && f.At != GalaxyMap.Capitals[0]);
        Assert.Contains(ShipClass.Fighter, direct.Planets[GalaxyMap.Capitals[0]].Repeat);
    }

    [Fact]
    public void Galaxy_commands_only_move_and_run_what_is_the_issuers()
    {
        var g = Campaign();
        var home = GalaxyMap.Capitals[0];
        var theirs = GalaxyMap.Capitals[1];
        var mine = Galaxies.FleetsAt(g, home, Klaed)[0];
        var foe = Galaxies.FleetsAt(g, theirs, Nairan)[0];
        var next = Galaxies.Neighbours(g, home)[0];
        GalaxyCommand[] refused =
        [
            new GalaxyCommand.SendFleet(foe.Id, next), new GalaxyCommand.RedirectFleet(foe.Id, next), new GalaxyCommand.StopFleet(foe.Id),
            new GalaxyCommand.CancelBuild(theirs, 0, QueueSide.Units), new GalaxyCommand.EnqueueBuild(theirs, GalaxyBuild.Ship(ShipClass.Scout)),
            new GalaxyCommand.SetRally(theirs, null), new GalaxyCommand.SetLoop(theirs, ShipClass.Scout, true),
            // Malformed: a planet off the map, unknown values, a count below zero (which would add ships), a hero twice.
            new GalaxyCommand.SendFleet(mine.Id, 999), new GalaxyCommand.EnqueueBuild(-1, GalaxyBuild.Ship(ShipClass.Scout)),
            new GalaxyCommand.EnqueueBuild(home, GalaxyBuild.Ship((ShipClass)42)), new GalaxyCommand.StartTech((TechId)42),
            new GalaxyCommand.SendFleet(mine.Id, next, new ShipCounts { [ShipClass.Fighter] = -5 }),
            new GalaxyCommand.ChooseBattle(1, BattleStatus.Pending),
        ];
        var before = Digest(g);
        foreach (var c in refused) Assert.False(Commands.Apply(g, Klaed, c).Ok, c.ToString());
        Assert.Equal(before, Digest(g));
        var hero = new HeroId(Faction.Klaed, HeroRole.Admiral);
        mine.Heroes.Add(hero);
        Assert.False(Commands.Apply(g, Klaed, new GalaxyCommand.SendFleet(mine.Id, next, new ShipCounts(), [hero, hero])).Ok);
        Assert.Single(mine.Heroes);
        // A battle: only its two sides choose, and only the attacker falls back.
        var world = CreateWorld(1000, 1000);
        var battle = new GalaxyBattle { Id = 77, PlanetId = next, Attacker = Nairan, Defender = Faction.Nautolan, From = theirs, World = world };
        g.Battles.Add(battle);
        Assert.False(Commands.Apply(g, Klaed, new GalaxyCommand.ChooseBattle(77, BattleStatus.Auto)).Ok);
        Assert.False(Commands.Apply(g, Klaed, new GalaxyCommand.DelegateBattle(77)).Ok);
        Assert.False(Commands.Apply(g, Klaed, new GalaxyCommand.Retreat(77)).Ok);
        Assert.False(Commands.Apply(g, Faction.Nautolan, new GalaxyCommand.Retreat(77)).Ok);
        Assert.Equal(BattleStatus.Pending, battle.Status);
        Assert.True(Commands.Apply(g, Faction.Nautolan, new GalaxyCommand.ChooseBattle(77, BattleStatus.Auto)).Ok);
        Assert.Equal(BattleStatus.Auto, battle.Status);
        Assert.True(Commands.Apply(g, Nairan, new GalaxyCommand.Retreat(77)).Ok);
        Assert.DoesNotContain(battle, g.Battles);
    }

    static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) write(w);
        return ms.ToArray();
    }

    [Fact]
    public void Commands_cross_the_wire_unchanged()
    {
        Command[] battle =
        [
            new Command.Move([1, 2, 300000], 10.5, -3, true), new Command.Attack([4], 5), new Command.Stop([]),
            new Command.UseAbility(7, AbilityId.OrbitalStrike, new Aim(1.25, 2.5, 9)), new Command.UseAbility(7, AbilityId.Sprint),
            new Command.UseAbility(8, AbilityId.Spinal, new Aim(3, 4)),
            new Command.EnqueueShip(ShipClass.Titan, 5), new Command.EnqueueHero(new HeroId(Faction.Nairan, HeroRole.Ace)), new Command.UpgradeStation(),
            new Command.UpgradeStructure(12), new Command.StartResearch(TechId.Energy), new Command.PlaceStructure(BuildableType.StarLance, 1, 2),
            new Command.CancelQueued(3, QueueSide.Buildings), new Command.CancelConstruction(13), new Command.CancelResearch(2),
            new Command.SetRally(100, 200), new Command.SetLoop(ShipClass.Bomber, true), new Command.SetAutoAbilities(true),
            new Command.StartPilot(14), new Command.StopPilot(14), new Command.PilotInput(14, -1, 0.5, 1e6, -7.125, true),
            new Command.Spawn(Faction.Errant, "colossus", 5, 6, Math.PI), new Command.GrantCredits(5000), new Command.SetFog(false),
            new Command.SetAI(new Side(5), Difficulty.Hard), new Command.SetAI(new Side(4), null), new Command.Destroy([1, 2]),
        ];
        foreach (var c in battle)
        {
            var bytes = Bytes(w => Commands.Write(w, c));
            var back = Commands.ReadCommand(new BinaryReader(new MemoryStream(bytes)));
            Assert.Equal(c.GetType(), back.GetType());
            Assert.Equal(bytes, Bytes(w => Commands.Write(w, back)));
        }
        // Every kind of command has its format.
        Assert.Equal(typeof(Command).GetNestedTypes().Length, battle.Select(c => c.GetType()).Distinct().Count());
        GalaxyCommand[] galaxy =
        [
            new GalaxyCommand.SendFleet(1, 2), new GalaxyCommand.SendFleet(1, 2, new ShipCounts { [ShipClass.Frigate] = 3 }, [new HeroId(Faction.Klaed, HeroRole.Engineer)]),
            new GalaxyCommand.RedirectFleet(3, 4), new GalaxyCommand.StopFleet(5), new GalaxyCommand.EnqueueBuild(6, GalaxyBuild.HeroOf(new HeroId(Faction.Nautolan, HeroRole.Admiral))),
            new GalaxyCommand.CancelBuild(7, 1, QueueSide.Units), new GalaxyCommand.SetLoop(8, ShipClass.Carrier, false), new GalaxyCommand.SetRally(9, null),
            new GalaxyCommand.SetRally(9, 10), new GalaxyCommand.StartTech(TechId.Hangars), new GalaxyCommand.CancelTech(0), new GalaxyCommand.FireLance(11, 12),
            new GalaxyCommand.OfferTruce(new Side(1)), new GalaxyCommand.AnswerTruce(new Side(2), true), new GalaxyCommand.BreakTruce(new Side(4)),
            new GalaxyCommand.ChooseBattle(13, BattleStatus.Tactical), new GalaxyCommand.DelegateBattle(14), new GalaxyCommand.Retreat(15),
            new GalaxyCommand.ProposeDeal(new Side(5), DealKind.Demand, 60, true), new GalaxyCommand.AnswerDeal(new Side(1), DealKind.Alliance, false),
            new GalaxyCommand.EndDeal(new Side(2), DealKind.Trade),
        ];
        foreach (var c in galaxy)
        {
            var bytes = Bytes(w => Commands.Write(w, c));
            var back = Commands.ReadGalaxyCommand(new BinaryReader(new MemoryStream(bytes)));
            Assert.Equal(c.GetType(), back.GetType());
            Assert.Equal(bytes, Bytes(w => Commands.Write(w, back)));
        }
        Assert.Equal(typeof(GalaxyCommand).GetNestedTypes().Length, galaxy.Select(c => c.GetType()).Distinct().Count());
        // Bytes that are no command throw, as does a list longer than any battle.
        Assert.Throws<InvalidDataException>(() => Commands.ReadCommand(new BinaryReader(new MemoryStream([200]))));
        Assert.Throws<InvalidDataException>(() => Commands.ReadGalaxyCommand(new BinaryReader(new MemoryStream([0]))));
        Assert.Throws<InvalidDataException>(() => Commands.ReadCommand(new BinaryReader(new MemoryStream([3, 0xff, 0xff, 0xff, 0x7f]))));
        Assert.Throws<EndOfStreamException>(() => Commands.ReadCommand(new BinaryReader(new MemoryStream([1, 1]))));
    }
}
