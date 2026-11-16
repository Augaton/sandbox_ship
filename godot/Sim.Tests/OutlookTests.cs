// A battle's likely outcome, as its prompt says it in words, against what the automatic resolution
// really does. The small sample always runs; the measure is long and only runs when asked, after
// any change to the rules of battle:
//   OUTLOOK=1 dotnet test Sim.Tests --filter Outlook --logger "console;verbosity=detailed"
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class OutlookTests(ITestOutputHelper output)
{
    static readonly Side A = Lobby.SideOf(0), D = Lobby.SideOf(1);
    const double AutoDt = 1.0 / 30;

    static World Fight(ShipCounts attackers, ShipCounts defenders, PlanetDefense defense, int seed = 3, Faction ra = Faction.Klaed, Faction rd = Faction.Nairan) =>
        Battle.CreatePlanetBattle(new BattleSide { Side = A, Race = ra, Ships = attackers }, new BattleSide { Side = D, Race = rd, Ships = defenders }, defense, seed);

    /// <summary>Plays a battle out as the galaxy resolves one on its own (Galaxies.AdvanceAutoBattles).</summary>
    static BattleResult Resolve(World w)
    {
        double t = 0;
        while (true)
        {
            Steps.StepWorld(w, AutoDt);
            Battle.UpdateBattleAI(w, [A, D], AutoDt);
            t += AutoDt;
            w.Events.Clear();
            if (Battle.Outcome(w, A, D) is BattleResult r) return r;
            if (t >= GalaxyRules.AutoTimeout) return BattleResult.Retreat;
        }
    }

    static BattleOutlook OutlookFor(World w) => Battle.Outlook(Battle.Power(w, A, D), Battle.Power(w, D, A));

    [Fact]
    public void Mines_and_docks_do_not_count_for_they_neither_fight_nor_have_to_fall()
    {
        var fleet = new ShipCounts { [ShipClass.Frigate] = 4 };
        var plain = Fight(fleet, fleet, new PlanetDefense { Station = 1 });
        var rich = Fight(fleet, fleet, new PlanetDefense { Station = 1, Mines = 3, RepairDock = 1 });
        Assert.Equal(Battle.Power(plain, D, A), Battle.Power(rich, D, A), 3);
        Assert.Equal(Battle.Power(plain, A, D), Battle.Power(rich, A, D), 3);
    }

    [Fact]
    public void A_station_counts_but_it_is_no_fleet_of_the_same_hull()
    {
        var raiders = new ShipCounts { [ShipClass.Frigate] = 4, [ShipClass.Scout] = 2 };
        var garrison = new ShipCounts { [ShipClass.Frigate] = 2, [ShipClass.Scout] = 2 };
        var open = Battle.Power(Fight(raiders, garrison, PlanetDefense.None), D, A);
        var w = Fight(raiders, garrison, new PlanetDefense { Station = 1 });
        Expect.Greater(Battle.Power(w, D, A), open);
        // Hull and shields alone would have this garrison crush its attackers; they beat it more often than not.
        var hulls = w.Entities.Where(e => e.Side == D).Sum(e => e.Hull + e.Shield) / w.Entities.Where(e => e.Side == A).Sum(e => e.Hull + e.Shield);
        Expect.Greater(hulls, 1.5);
        Assert.NotEqual(BattleOutlook.CertainDefeat, OutlookFor(w));
        Assert.NotEqual(BattleOutlook.Unfavorable, OutlookFor(w));
    }

    [Fact]
    public void Words_follow_the_attackers_edge_and_each_side_reads_the_other_way()
    {
        Assert.Equal(BattleOutlook.Even, Battle.Outlook(0, 0));
        Assert.Equal(BattleOutlook.CertainVictory, Battle.Outlook(1, 0));
        Assert.Equal(BattleOutlook.CertainDefeat, Battle.Outlook(0, 1));
        Assert.Equal(BattleOutlook.CertainVictory, Battle.Outlook(6, 1));
        Assert.Equal(BattleOutlook.Favorable, Battle.Outlook(5.9, 1));
        Assert.Equal(BattleOutlook.Favorable, Battle.Outlook(2, 1));
        Assert.Equal(BattleOutlook.Even, Battle.Outlook(1.9, 1));
        Assert.Equal(BattleOutlook.Even, Battle.Outlook(0.7, 1));
        Assert.Equal(BattleOutlook.Unfavorable, Battle.Outlook(0.6, 1));
        Assert.Equal(BattleOutlook.Unfavorable, Battle.Outlook(0.25, 1));
        Assert.Equal(BattleOutlook.CertainDefeat, Battle.Outlook(0.2, 1));
    }

    /// <summary>A campaign of two people where Alice's capital is attacked: the battle waits for both.</summary>
    static (Galaxy G, GalaxyBattle B, Side Alice, Side Bob) Attacked(ShipCounts by)
    {
        var (alice, bob) = (Lobby.SideOf(0), Lobby.SideOf(1));
        var g = CreateGalaxy([new Slot(alice, Faction.Klaed), new Slot(bob, Faction.Nairan)], 4, errants: false, incidents: false);
        g.Player = alice;
        g.Humans[alice] = "Alice";
        g.Humans[bob] = "Bob";
        var home = g.Planets.First(p => p.Owner == alice);
        AddShipsAt(g, bob, home.Id, by)!.ArrivedAt = 1;
        StepGalaxy(g);
        return (g, Assert.Single(g.Battles), alice, bob);
    }

    [Fact]
    public void A_battle_reads_the_same_on_a_replica_and_opposite_for_each_side()
    {
        var (g, b, alice, bob) = Attacked(new ShipCounts { [ShipClass.Scout] = 1 });
        Assert.Equal(BattleOutlook.CertainVictory, OutlookOf(b, alice));
        Assert.Equal(BattleOutlook.CertainDefeat, OutlookOf(b, bob));
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) GalaxySnapshot.Write(g, bob, w);
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        Assert.NotNull(GalaxySnapshot.Read(new BinaryReader(new MemoryStream(ms.ToArray())), replica));
        var seen = Assert.Single(replica.Battles);
        Assert.NotNull(seen.Forces);
        Assert.Equal(BattleOutlook.CertainDefeat, OutlookOf(seen, bob));
        Assert.Equal(BattleOutlook.CertainVictory, OutlookOf(seen, alice));
        Assert.Equal(ForceOf(b, bob).Power, ForceOf(seen, bob).Power, 0);
    }

    [Fact]
    public void One_side_resolving_a_battle_alone_decides_nothing_for_the_other()
    {
        var (g, b, alice, bob) = Attacked(new ShipCounts { [ShipClass.Scout] = 1 });
        // What the galaxy screen sends for a player whose battle looks won (GalaxyScreen.ResolveAlone).
        Assert.True(Commands.Apply(g, alice, new GalaxyCommand.ChooseBattle(b.Id, BattleStatus.Auto)).Ok);
        Assert.Equal(BattleStatus.Pending, b.Status);
        Assert.False(b.Choices.ContainsKey(bob));
        Assert.True(WaitingForPlayer(g));
        Assert.True(Commands.Apply(g, bob, new GalaxyCommand.ChooseBattle(b.Id, BattleStatus.Tactical)).Ok);
        Assert.Equal(BattleStatus.Tactical, b.Status);
        Assert.Equal([bob], b.Commanders);
    }

    /// <summary>A battle drawn at random, as no AI would choose it: fleets of a few classes, a planet's defenses, research and wear.</summary>
    static World Drawn(int seed)
    {
        const double most = 16000;
        var rng = new Random(seed);
        Faction[] races = [Faction.Klaed, Faction.Nairan, Faction.Nautolan];
        var (ra, rd) = (races[rng.Next(3)], races[rng.Next(3)]);
        var budget = Math.Exp(rng.NextDouble() * Math.Log(most / 150)) * 150;
        var station = rng.NextDouble() switch { < 0.35 => 0, < 0.65 => 1, < 0.85 => 2, < 0.95 => 3, _ => 4 };
        var against = Math.Min(most, budget * Math.Exp((rng.NextDouble() * 2 - 1) * 1.8));
        if (station > 0 && rng.NextDouble() < 0.3) against = 0;
        var defense = station == 0 && rng.NextDouble() < 0.4 ? PlanetDefense.None : new PlanetDefense
        {
            Station = station, Platforms = station > 0 ? rng.Next(0, station * 2 + 1) : 0, Mines = rng.Next(0, 4),
            RepairDock = station > 0 && rng.NextDouble() < 0.3 ? 1 : 0, IonCannon = station >= 2 && rng.NextDouble() < 0.3 ? 1 : 0,
            PlatformTier = rng.Next(1, Math.Min(3, Math.Max(1, station)) + 1),
        };
        if (station == 0) against = Math.Max(against, 100);
        var att = new BattleSide { Side = A, Race = ra, Ships = Fleet(rng, budget), Research = Levels(rng), Condition = Wear(rng) };
        var def = new BattleSide { Side = D, Race = rd, Ships = Fleet(rng, against), Research = Levels(rng), Condition = Wear(rng) };
        return Battle.CreatePlanetBattle(att, def, defense, rng.Next(1, int.MaxValue));
    }

    static double Wear(Random rng) => rng.NextDouble() < 0.7 ? 1 : 0.4 + rng.NextDouble() * 0.6;

    static ShipCounts Fleet(Random rng, double budget)
    {
        ShipClass[] yard = [.. Ships.All.Where(c => !Ships.Defs[c].FromHangar && c != ShipClass.Colossus)];
        var doctrine = yard.OrderBy(_ => rng.Next()).Take(rng.Next(1, 5)).ToList();
        var ships = new ShipCounts();
        for (var tries = 0; budget > 0 && tries < 400; tries++)
        {
            var c = doctrine[rng.Next(doctrine.Count)];
            if (Ships.UnitValue(c) > budget || (Ships.Defs[c].Limit is int limit && ships.Of(c) >= limit)) continue;
            ships.Increase(c);
            budget -= Ships.UnitValue(c);
        }
        if (Battle.CountShips(ships) == 0 && budget > 0) ships.Increase(ShipClass.Scout);
        return ships;
    }

    static ResearchLevels Levels(Random rng)
    {
        var r = new ResearchLevels();
        if (rng.NextDouble() < 0.5) return r;
        foreach (var t in Techs.All) if (rng.NextDouble() < 0.4) r[t] = rng.Next(1, Math.Min(4, Techs.Defs[t].MaxLevel + 1));
        return r;
    }

    /// <summary>How many battles each word covered for the attacker, and how many it won.</summary>
    static Dictionary<BattleOutlook, (int Battles, int Won)> Tally(IEnumerable<(BattleOutlook Word, BattleResult Result)> battles)
    {
        var tally = Enum.GetValues<BattleOutlook>().ToDictionary(o => o, _ => (Battles: 0, Won: 0));
        foreach (var (word, result) in battles)
        {
            var (n, won) = tally[word];
            tally[word] = (n + 1, won + (result == BattleResult.Attacker ? 1 : 0));
        }
        return tally;
    }

    [Fact]
    public void Almost_certain_means_almost_certain_on_a_small_sample()
    {
        // Fleets of random classes, one eight times the worth of the other, either way round.
        var outcomes = new (BattleOutlook, BattleResult)[32];
        Parallel.For(0, outcomes.Length, i =>
        {
            var rng = new Random(i * 41 + 7);
            var (attackers, defenders) = i % 2 == 0 ? (Fleet(rng, 1600), Fleet(rng, 200)) : (Fleet(rng, 200), Fleet(rng, 1600));
            var w = Fight(attackers, defenders, PlanetDefense.None, rng.Next(1, int.MaxValue));
            outcomes[i] = (OutlookFor(w), Resolve(w));
        });
        var t = Tally(outcomes);
        output.WriteLine(string.Join("\n", t.Select(kv => $"{kv.Key}: {kv.Value.Won}/{kv.Value.Battles}")));
        Expect.GreaterOrEqual(t[BattleOutlook.CertainVictory].Battles, 8);
        Expect.GreaterOrEqual(t[BattleOutlook.CertainDefeat].Battles, 8);
        // At most one surprise at either end: the words a player may let the game act on.
        Expect.GreaterOrEqual(t[BattleOutlook.CertainVictory].Won, t[BattleOutlook.CertainVictory].Battles - 1);
        Expect.Less(t[BattleOutlook.CertainDefeat].Won, 2);
    }

    [Fact]
    public void Measures_the_outlook_against_the_automatic_resolution()
    {
        if (Environment.GetEnvironmentVariable("OUTLOOK") is null) return;
        var drawn = new (BattleOutlook, BattleResult)[1200];
        Parallel.For(0, drawn.Length, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i =>
        {
            var w = Drawn(i * 37 + 11);
            drawn[i] = (OutlookFor(w), Resolve(w));
        });
        Report("Batailles tirées au sort (aucune IA ne les choisit)", drawn);
        (MapSize? Size, int Players)[] maps = [(null, 2), (MapSize.Small, 2), (MapSize.Medium, 2), (MapSize.Medium, 3), (MapSize.Large, 4)];
        var games = (from m in maps from s in Enumerable.Range(1, 6) select (m.Size, m.Players, Seed: s * 11 + 3)).ToList();
        var played = new List<(BattleOutlook, BattleResult)>[games.Count];
        Parallel.For(0, games.Count, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i => played[i] = Played(games[i].Size, games[i].Players, games[i].Seed));
        Report("Batailles de campagnes entre IA (l'IA attaque quand elle se croit plus forte)", [.. played.SelectMany(x => x)]);
    }

    void Report(string title, IReadOnlyList<(BattleOutlook Word, BattleResult Result)> battles)
    {
        output.WriteLine($"{title} : {battles.Count}");
        foreach (var (word, (n, won)) in Tally(battles).OrderByDescending(kv => kv.Key))
        {
            if (n > 0) output.WriteLine($"  attaquant {word,-15} {n,5} batailles, il l'emporte dans {100.0 * won / n,5:0.0} %");
        }
    }

    /// <summary>A campaign between AIs: what each battle looked like when it started, and how it ended.</summary>
    static List<(BattleOutlook, BattleResult)> Played(MapSize? size, int players, int seed)
    {
        Faction[] races = [Faction.Klaed, Faction.Nairan, Faction.Nautolan, Faction.Klaed];
        var slots = Enumerable.Range(0, players).Select(i => new Slot(Lobby.SideOf(i), races[i], 0, Difficulty.Normal, null, Personalities.Draw(seed, i))).ToList();
        var g = CreateGalaxy(slots, seed, size is MapSize m ? GalaxyGen.GenerateLayout(seed, players, m) : GalaxyGen.Classic);
        g.Player = (Faction)99;
        var open = new List<(Side Attacker, Side Defender, int Planet, BattleOutlook Word)>();
        var results = new List<(BattleOutlook, BattleResult)>();
        while (g.Winner is null && g.Time < 3600)
        {
            StepGalaxy(g);
            foreach (var e in g.Events)
            {
                if (e is BattleStarted s && g.Battles.Find(x => x.Id == s.BattleId) is { } b) open.Add((b.Attacker, b.Defender, b.PlanetId, OutlookOf(b, b.Attacker)));
                else if (e is BattleEnded end && open.FindIndex(o => o.Attacker == end.Attacker && o.Defender == end.Defender && o.Planet == end.PlanetId) is var at and >= 0)
                {
                    results.Add((open[at].Word, end.Result));
                    open.RemoveAt(at);
                }
            }
            g.Events.Clear();
        }
        return results;
    }
}
