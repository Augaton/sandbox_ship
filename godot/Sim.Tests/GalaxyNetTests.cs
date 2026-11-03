// The galaxy campaign played over the network (not in the web version), host and players in one
// process: galaxy snapshots and their fog, a replica kept up by them, battles that wait for every
// human side in them, the players' commands applied at the next step, the notices that must not
// be lost sent apart, and a saved campaign resumed with its players back at their seats.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class GalaxyNetTests(ITestOutputHelper output)
{
    const int Bob = 1001;
    /// <summary>The host (Alice) on the first seat, Bob on the second, a bot on the third.</summary>
    static readonly Side Alice = Lobby.SideOf(0), Bobs = Lobby.SideOf(1), Bot = Lobby.SideOf(2);

    /// <summary>A campaign of two people and a bot, as a hosted lobby makes it.</summary>
    static Galaxy Campaign(int seed = 4, MapSize size = MapSize.Small, int seats = 3)
    {
        var l = Lobby.Create(LobbyMode.Campaign, seed);
        l.Size = size;
        l.SetKind(l.Seats[1], SeatKind.Human, Bob);
        while (l.Seats.Count < seats) l.AddBot();
        var g = CreateGalaxy(l.Slots(), seed, l.Layout());
        g.Player = Alice;
        g.Humans[Alice] = "Alice";
        g.Humans[Bobs] = "Bob";
        return g;
    }

    /// <summary>Everyone on the AI for a while, so that fleets move, planets change hands and battles start; then the people back.</summary>
    static void PlayAhead(Galaxy g, double seconds)
    {
        var humans = g.Humans.ToDictionary();
        foreach (var side in humans.Keys) g.Factions[side].AI = new GalaxyAIState();
        g.Humans.Clear();
        var player = g.Player;
        g.Player = Faction.Errant;
        RunGalaxy(g, seconds);
        g.Player = player;
        foreach (var (side, name) in humans)
        {
            g.Humans[side] = name;
            g.Factions[side].AI = null;
        }
    }

    static byte[] Bytes(Galaxy g, Side viewer, IReadOnlyList<GalaxyEvent>? events = null, GalaxyClock clock = default)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) GalaxySnapshot.Write(g, viewer, w, events, null, clock);
        return ms.ToArray();
    }

    /// <summary>A replica of `g` from one of its snapshots, its chart read first.</summary>
    static Galaxy Replica(Galaxy g, byte[] bytes)
    {
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        Assert.NotNull(GalaxySnapshot.Read(new BinaryReader(new MemoryStream(bytes)), replica));
        return replica;
    }

    [Fact]
    public void A_replica_writes_back_the_very_bytes_it_was_read_from()
    {
        var g = Campaign();
        PlayAhead(g, 600);
        Assert.NotEmpty(g.Events);
        var clock = new GalaxyClock(2, true);
        foreach (var viewer in new[] { Alice, Bobs })
        {
            var bytes = Bytes(g, viewer, g.Events, clock);
            var replica = Replica(g, bytes);
            Assert.Equal(viewer, replica.Player);
            Assert.Equal(bytes, Bytes(replica, viewer, replica.Events, clock));
            Assert.Equal(GalaxySnapshot.Chart(g), GalaxySnapshot.Chart(replica));
        }
    }

    [Fact]
    public void A_snapshot_before_the_chart_changes_nothing_and_the_chart_goes_once()
    {
        var g = Campaign();
        PlayAhead(g, 120);
        var replica = new Galaxy();
        Assert.Null(GalaxySnapshot.Read(new BinaryReader(new MemoryStream(Bytes(g, Bobs))), replica));
        Assert.Empty(replica.Planets);
        Assert.Empty(replica.Fleets);
        Assert.Equal(0, replica.Time);
        // A chart of another version or cut short changes nothing either.
        var chart = GalaxySnapshot.Chart(g);
        var broken = (byte[])chart.Clone();
        broken[4]++;
        Assert.Throws<InvalidDataException>(() => GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(broken)), replica));
        Assert.Throws<EndOfStreamException>(() => GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(chart[..^2])), replica));
        Assert.Empty(replica.Planets);
        // Served: the chart with the first snapshot only, before it.
        var t = new Table(g);
        for (var i = 0; i < 120; i++) t.Frame();
        Assert.IsType<Message.Chart>(t.Messages[0]);
        Assert.Single(t.Messages.OfType<Message.Chart>());
        Expect.Greater(t.Messages.OfType<Message.Map>().Count(), 5);
        Assert.Equal(g.Planets.Select(p => (p.Name, (float)p.X, p.Owner)), t.Replica.Planets.Select(p => (p.Name, (float)p.X, p.Owner)));
        Assert.Equal(g.Lanes, t.Replica.Lanes);
    }

    [Fact]
    public void A_replica_knows_only_what_its_viewers_team_knows()
    {
        var g = Campaign();
        var bobHome = g.Planets.First(p => p.Owner == Bobs);
        var aliceHome = g.Planets.First(p => p.Owner == Alice);
        bobHome.Station = 3;
        bobHome.Units.Add(new GalaxyQueueItem { Kind = GalaxyBuildKind.Ship, Cls = ShipClass.Frigate, Remaining = 5, Total = 10, Cost = 100 });
        bobHome.Rally = Neighbours(g, bobHome.Id)[0];
        g.Factions[Bobs].Credits = 4321;
        g.Factions[Alice].Credits = 1234;
        // Bob's eyes do not reach Alice's capital, the bot offered Alice a truce, and holds a grudge against Bob.
        Assert.DoesNotContain(aliceHome.Id, VisiblePlanets(g, Bobs));
        g.Offers.Add(new DealOffer { From = Bot, To = Alice, Remaining = 30 });
        g.Factions[Bot].AI!.Grudges[Bobs] = 120;
        g.Factions[Bot].AI!.Grudges[Alice] = 60;

        var replica = Replica(g, Bytes(g, Bobs));
        // Ownership everywhere; what guards a planet, only where Bob has eyes.
        Assert.Equal(g.Planets.Select(p => p.Owner), replica.Planets.Select(p => p.Owner));
        Assert.Equal(0, replica.Planets[aliceHome.Id].Station);
        Assert.Equal(3, replica.Planets[bobHome.Id].Station);
        // Alice's fleet at her capital is out of sight; Bob's own fleets are all there.
        Assert.DoesNotContain(replica.Fleets.Values, f => f.Side == Alice);
        Assert.Equal(g.Fleets.Values.Where(f => f.Side == Bobs).Select(f => f.Id), replica.Fleets.Values.Where(f => f.Side == Bobs).Select(f => f.Id));
        // Bob's economy in full, nobody else's.
        Assert.Equal(4321, replica.Factions[Bobs].Credits);
        Assert.Equal(0, replica.Factions[Alice].Credits);
        Assert.Single(replica.Planets[bobHome.Id].Units);
        Assert.Equal(bobHome.Rally, replica.Planets[bobHome.Id].Rally);
        // Offers between others stay theirs; the bot's grudge against Bob is his to know, not the one against Alice.
        Assert.Empty(replica.Offers);
        Assert.Equal([new KeyValuePair<Side, double>(Bobs, 120)], replica.Factions[Bot].AI!.Grudges);
        Assert.Null(replica.Factions[Alice].AI);
        Assert.Equal(g.Humans, replica.Humans);

        // Alice's own queues never show to Bob, even where he has eyes.
        var aliceSees = Replica(g, Bytes(g, Alice));
        Assert.Empty(aliceSees.Planets[bobHome.Id].Units);
        Assert.Single(aliceSees.Offers);
    }

    [Fact]
    public void A_battle_waits_until_every_human_side_in_it_has_chosen()
    {
        var g = Campaign(seats: 2);
        var bobHome = g.Planets.First(p => p.Owner == Bobs).Id;
        AddShipsAt(g, Alice, bobHome, new ShipCounts { [ShipClass.Frigate] = 3 });
        StepGalaxy(g);
        var b = Assert.Single(g.Battles);
        Assert.Equal(BattleStatus.Pending, b.Status);
        Assert.Equal([Alice, Bobs], HumansIn(g, b));
        Assert.True(WaitingForPlayer(g));

        // Alice wants to command it: it still waits for Bob.
        Assert.True(Commands.Apply(g, Alice, new GalaxyCommand.ChooseBattle(b.Id, BattleStatus.Tactical)).Ok);
        Assert.Equal(BattleStatus.Pending, b.Status);
        var time = g.Time;
        StepGalaxy(g);
        Assert.Equal(time, g.Time);
        // Bob leaves it to the AI: Alice commands it, the AI plays Bob's side.
        Commands.Apply(g, Bobs, new GalaxyCommand.ChooseBattle(b.Id, BattleStatus.Auto));
        Assert.Equal(BattleStatus.Tactical, b.Status);
        Assert.Equal([Alice], b.Commanders);
        // A side out of the battle has no say.
        Assert.False(Commands.Apply(g, Bot, new GalaxyCommand.ChooseBattle(b.Id, BattleStatus.Auto)).Ok);
        // Alice leaves it too: nobody commands it, it plays on by itself.
        Commands.Apply(g, Alice, new GalaxyCommand.DelegateBattle(b.Id));
        Assert.Equal(BattleStatus.Auto, b.Status);
        Assert.False(WaitingForPlayer(g));
    }

    [Fact]
    public void Both_humans_command_and_a_battle_goes_on_without_whoever_leaves_it()
    {
        var g = Campaign(seats: 2);
        var bobHome = g.Planets.First(p => p.Owner == Bobs).Id;
        AddShipsAt(g, Alice, bobHome, new ShipCounts { [ShipClass.Frigate] = 3 });
        StepGalaxy(g);
        var b = g.Battles[0];
        Choose(g, Bobs, b.Id, BattleStatus.Tactical);
        Choose(g, Alice, b.Id, BattleStatus.Tactical);
        Assert.Equal([Alice, Bobs], b.Commanders);
        Delegate(g, Alice, b.Id);
        Assert.Equal(BattleStatus.Tactical, b.Status);
        Assert.Equal([Bobs], b.Commanders);
        // Bob leaves the game: his side to the AI, the battle to itself.
        HandToAI(g, Bobs);
        Assert.Equal(BattleStatus.Auto, b.Status);
        Assert.False(Human(g, Bobs));
        Assert.NotNull(g.Factions[Bobs].AI);
    }

    [Fact]
    public void All_humans_choosing_to_resolve_it_resolve_it_on_its_own_and_one_who_leaves_counts_no_more()
    {
        var g = Campaign(seats: 2);
        var bobHome = g.Planets.First(p => p.Owner == Bobs).Id;
        AddShipsAt(g, Alice, bobHome, new ShipCounts { [ShipClass.Frigate] = 3 });
        StepGalaxy(g);
        var b = g.Battles[0];
        Choose(g, Alice, b.Id, BattleStatus.Auto);
        Assert.Equal(BattleStatus.Pending, b.Status);
        Choose(g, Bobs, b.Id, BattleStatus.Auto);
        Assert.Equal(BattleStatus.Auto, b.Status);
        Assert.Empty(b.Commanders);
        RunGalaxy(g, 120);
        Assert.Empty(g.Battles);

        // Another battle: Alice chose, then Bob leaves before choosing; it goes as she chose.
        AddShipsAt(g, Alice, bobHome, new ShipCounts { [ShipClass.Battlecruiser] = 2 });
        StepGalaxy(g);
        var next = Assert.Single(g.Battles);
        Choose(g, Alice, next.Id, BattleStatus.Tactical);
        HandToAI(g, Bobs);
        Assert.Equal(BattleStatus.Tactical, next.Status);
        Assert.Equal([Alice], next.Commanders);
    }

    [Fact]
    public void Alone_a_battle_is_chosen_at_once_as_it_always_was()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        var enemy = g.Planets.First(p => p.Owner == Faction.Nairan).Id;
        AddShipsAt(g, Faction.Klaed, enemy, new ShipCounts { [ShipClass.Frigate] = 3 });
        StepGalaxy(g);
        var b = g.Battles[0];
        Assert.Equal(BattleStatus.Pending, b.Status);
        Commands.Apply(g, Faction.Klaed, new GalaxyCommand.ChooseBattle(b.Id, BattleStatus.Tactical));
        Assert.Equal(BattleStatus.Tactical, b.Status);
        Assert.Equal([new Side(0)], b.Commanders);
        Commands.Apply(g, Faction.Klaed, new GalaxyCommand.DelegateBattle(b.Id));
        Assert.Equal(BattleStatus.Auto, b.Status);
    }

    /// <summary>The host plays Alice; Bob plays from afar. Every message goes through its bytes.</summary>
    sealed class Table
    {
        public readonly Galaxy Galaxy;
        public readonly GalaxyHost Host;
        public readonly Galaxy Replica = new();
        public readonly GalaxyMotion Motion = new();
        public readonly List<string> Refusals = [];
        public readonly List<GalaxyEvent> Heard = [];
        public readonly List<Message> Messages = [];
        public double Now;

        public Table(Galaxy g)
        {
            Galaxy = g;
            Host = new GalaxyHost(g, Deliver);
            Host.Add(Bob, Bobs);
        }

        void Deliver(int peer, Message m)
        {
            var back = Protocol.Decode(Protocol.Encode(m));
            Messages.Add(back);
            switch (back)
            {
                case Message.Chart c:
                    GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(c.Bytes)), Replica);
                    break;
                case Message.Map s:
                    Assert.True(Motion.Read(new BinaryReader(new MemoryStream(s.Snapshot)), Replica, Now));
                    break;
                case Message.Tidings t:
                    GalaxySnapshot.ReadEvents(new BinaryReader(new MemoryStream(t.Events)), Replica);
                    break;
                case Message.Refused r: Refusals.Add(r.Reason); break;
            }
            Heard.AddRange(Replica.Events);
            Replica.Events.Clear();
        }

        public void Order(GalaxyCommand c) => Host.Receive(Bob, ((Message.Plan)Protocol.Decode(Protocol.Encode(new Message.Plan(c)))).Command);

        /// <summary>One frame of the host at 60 images a second, the galaxy at ×6: one step, the events kept, the snapshots due.</summary>
        public void Frame()
        {
            var before = Galaxy.Events.Count;
            Host.BeforeStep();
            StepGalaxy(Galaxy);
            Host.Collect(Galaxy.Events.Skip(before));
            Galaxy.Events.Clear();
            Now += 1 / 60.0;
            Host.Tick(1 / 60.0);
            Motion.Advance(Replica, 1 / 60.0);
        }
    }

    [Fact]
    public void A_players_orders_wait_for_the_next_step_and_those_refused_are_answered()
    {
        var t = new Table(Campaign(seats: 2));
        var g = t.Galaxy;
        var bobFleet = g.Fleets.Values.First(f => f.Side == Bobs);
        var aliceFleet = g.Fleets.Values.First(f => f.Side == Alice);
        var dest = Neighbours(g, bobFleet.At!.Value)[0];
        t.Order(new GalaxyCommand.SendFleet(bobFleet.Id, dest));
        t.Order(new GalaxyCommand.SendFleet(aliceFleet.Id, dest));
        Assert.NotNull(bobFleet.At);
        t.Frame();
        Assert.Null(bobFleet.At);
        Assert.NotNull(aliceFleet.At);
        Assert.Equal(["Pas sous tes ordres"], t.Refusals);
        // A player who leaves hands its side to the AI.
        t.Host.Drop(Bob);
        Assert.NotNull(g.Factions[Bobs].AI);
        Assert.Empty(t.Host.Peers);
    }

    [Fact]
    public void A_player_cannot_flood_another_with_offers_refused_and_made_again()
    {
        var t = new Table(Campaign(seats: 2));
        var g = t.Galaxy;
        var offered = 0;
        // Ten seconds of a truce offered every frame, the host turning each one down at once.
        for (var i = 0; i < 600; i++)
        {
            t.Order(new GalaxyCommand.OfferTruce(Alice));
            t.Order(new GalaxyCommand.ProposeDeal(Alice, DealKind.Trade));
            t.Frame();
            offered += t.Heard.OfType<DealOffered>().Count(o => o.From == Bobs);
            t.Heard.Clear();
            Galaxies.RefuseDeal(g, Alice, Bobs, DealKind.Truce);
            Galaxies.RefuseDeal(g, Alice, Bobs, DealKind.Trade);
        }
        Expect.Less(offered, GalaxyHost.OfferBurst + 10 / GalaxyHost.OfferEvery + 1);
        Assert.Contains(GalaxyHost.TooManyOffers, t.Refusals);
        // Another kind of order still goes through.
        var fleet = g.Fleets.Values.First(f => f.Side == Bobs);
        t.Order(new GalaxyCommand.SendFleet(fleet.Id, Neighbours(g, fleet.At!.Value)[0]));
        t.Frame();
        Assert.Null(fleet.At);
    }

    [Fact]
    public void A_replica_kept_up_by_snapshots_matches_the_host_on_all_its_viewer_knows()
    {
        var g = Campaign(seats: 3);
        // Everyone plays by itself, Bob too: his replica must follow a galaxy that moves.
        foreach (var f in g.Factions.Values) f.AI ??= new GalaxyAIState();
        g.Humans.Clear();
        g.Player = Faction.Errant;
        var t = new Table(g);
        var checks = 0;
        for (var frame = 0; frame < 60 * 60 * 3; frame++)
        {
            for (var i = 0; i < 5; i++) StepGalaxy(g);
            t.Frame();
            if (frame % 97 != 0) continue;
            t.Host.SendAll();
            t.Motion.Advance(t.Replica, 10);
            Same(g, t.Replica, Bobs);
            checks++;
        }
        Assert.True(checks > 100);
        // What befell Bob reached him: his captures, his buildings, his battles.
        Assert.Contains(t.Heard, e => e is PlanetBuilt { Side: var s } && s == Bobs);
        Assert.Contains(t.Heard, e => e is Captured);
        output.WriteLine($"{g.Time:0} s de galaxie, {t.Host.Snapshots} instantanés, {t.Heard.Count} événements entendus, {t.Host.BytesTo(Bob) / t.Host.Snapshots} octets en moyenne");
    }

    /// <summary>The replica holds what the host holds, for all its viewer's team knows.</summary>
    static void Same(Galaxy host, Galaxy replica, Side viewer)
    {
        var seen = VisiblePlanets(host, viewer);
        Assert.Equal(host.Time, replica.Time);
        Assert.Equal(host.Winner, replica.Winner);
        foreach (var p in host.Planets)
        {
            var r = replica.Planets[p.Id];
            Assert.Equal(p.Owner, r.Owner);
            if (!Known(host, viewer, p, seen)) continue;
            Assert.Equal((p.Station, p.Mines, p.Platforms, p.PlatformTier, p.IonCannon, p.RepairDock, p.StarLance),
                (r.Station, r.Mines, r.Platforms, r.PlatformTier, r.IonCannon, r.RepairDock, r.StarLance));
            if (p.Owner == viewer) Assert.Equal(p.Units.Select(q => (q.Kind, q.Cls)), r.Units.Select(q => (q.Kind, q.Cls)));
        }
        var fleets = host.Fleets.Values.Where(f => FleetVisible(host, viewer, f, seen) || f.Side == Faction.Errant).ToList();
        Assert.Equal(fleets.Select(f => f.Id), replica.Fleets.Keys);
        foreach (var f in fleets)
        {
            var r = replica.Fleets[f.Id];
            Assert.Equal((f.Side, f.At, f.From, f.To, f.BattleId), (r.Side, r.At, r.From, r.To, r.BattleId));
            Assert.Equal(f.Progress, r.Progress, 1e-5);
            Assert.Equal(f.Ships.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key), r.Ships.OrderBy(kv => kv.Key));
        }
        Assert.Equal(Math.Floor(host.Factions[viewer].Credits), Math.Floor(replica.Factions[viewer].Credits));
        foreach (var b in replica.Battles) Assert.Contains(host.Battles, h => h.Id == b.Id && h.Status == b.Status);
        Assert.Equal(host.Ongoing.Select(i => (i.Kind, i.Planet, i.Other)), replica.Ongoing.Select(i => (i.Kind, i.Planet, i.Other)));
    }

    [Fact]
    public void Notices_go_apart_on_the_reliable_channel_and_a_snapshot_keeps_only_the_rest()
    {
        var t = new Table(Campaign(seats: 2));
        var g = t.Galaxy;
        var aliceHome = g.Planets.First(p => p.Owner == Alice).Id;
        // Bob attacks Alice's capital: the battle starts, then Alice loses a planet she had taken.
        AddShipsAt(g, Bobs, aliceHome, new ShipCounts { [ShipClass.Frigate] = 3 });
        var neutral = g.Planets.First(p => p.Owner is null && VisiblePlanets(g, Bobs).Contains(p.Id));
        t.Frame();
        neutral.Owner = Alice;
        g.Events.Add(new Captured(neutral.Id, Alice, null));
        t.Host.Collect(g.Events);
        g.Events.Clear();
        t.Host.SendAll();
        var tidings = t.Messages.OfType<Message.Tidings>().ToList();
        Assert.Single(tidings);
        Assert.Contains(t.Heard, e => e is Captured);
        // The battle starting came with the snapshot: the battles it lists tell it anyway.
        Assert.Contains(t.Heard, e => e is BattleStarted);
        var replica = Replica(g, t.Messages.OfType<Message.Map>().Last().Snapshot);
        Assert.DoesNotContain(replica.Events, e => e is Captured);
    }

    [Fact]
    public void A_battles_victory_goes_apart_on_the_reliable_channel()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], Seed = 2, Size = MapSize.Small });
        var sent = new List<Message>();
        var host = new BattleHost(w, (_, m) => sent.Add(Protocol.Decode(Protocol.Encode(m))));
        host.Add(Bob, Faction.Nairan);
        Worlds.StationOf(w, Faction.Nairan)!.Hull = 0;
        for (var i = 0; i < 5 && w.Winner is null; i++)
        {
            host.BeforeStep();
            Steps.StepWorld(w);
            host.Collect();
            w.Events.Clear();
        }
        Assert.NotNull(w.Winner);
        host.Tick(1);
        var news = Assert.Single(sent.OfType<Message.News>());
        var told = new World();
        Snapshot.ReadEvents(new BinaryReader(new MemoryStream(news.Events)), told);
        Assert.Contains(told.Events, e => e is Victory);
        Assert.Contains(told.Events, e => e is Destroyed { EntityType: "station" });
        var replica = new World();
        var frames = new SnapshotFrames();
        foreach (var s in sent.OfType<Message.State>()) Snapshot.ReadPart(new BinaryReader(new MemoryStream(s.Snapshot)), replica, frames);
        Assert.DoesNotContain(replica.Events, e => e is Victory or Destroyed { EntityType: "station" });
        Assert.Equal(w.Winner, replica.Winner);
    }

    [Fact]
    public void A_watcher_sees_the_battle_through_a_sides_eyes_and_gives_no_order()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], Seed = 2, Size = MapSize.Small });
        var sent = new List<(int Peer, Message M)>();
        var host = new BattleHost(w, (peer, m) => sent.Add((peer, m)));
        host.Add(Bob, Faction.Nairan, watches: true);
        var ship = Worlds.SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 500, 500, 0);
        host.Receive(Bob, new Command.Stop([ship.Id]));
        host.BeforeStep();
        Assert.Equal("Tu regardes la bataille", Assert.IsType<Message.Refused>(Assert.Single(sent).M).Reason);
        // Leaving, it hands nothing to the AI: it played nothing.
        Assert.Null(host.Remove(Bob));
        Assert.Null(w.Factions[Faction.Nairan].AI);
    }

    [Fact]
    public void A_campaign_saved_over_the_network_resumes_with_its_players_back_at_their_seats()
    {
        var g = Campaign(seats: 3);
        g.Factions[Bot].AI!.Difficulty = Difficulty.Hard;
        var path = Path.Combine(Path.GetTempPath(), $"sandbox-ship-net-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(GalaxySave.Save(g, path));
            Assert.Equal("Bob", GalaxySave.Info(path)!.Others);
            var saved = GalaxySave.Load(path)!;
            Assert.Equal(g.Humans, saved.Humans);

            var lobby = Lobby.Resume(saved, 9)!;
            Assert.True(lobby.Resumed);
            Assert.Equal([SeatKind.Human, SeatKind.Open, SeatKind.Bot], lobby.Seats.Select(s => s.Kind));
            Assert.Equal("Bob", lobby.Seats[1].Reserved);
            Assert.Equal(Difficulty.Hard, lobby.Seats[2].Difficulty);
            Assert.Equal(saved.Factions.Values.Select(f => (f.Race, f.Team, f.Color)), lobby.Seats.Select(s => (s.Race, s.Team, s.Color)));
            Assert.Contains(lobby.Problems(), p => p.StartsWith("En attente de Bob", StringComparison.Ordinal));

            // Over the wire, as a player gets it.
            var room = new LobbyRoom(((Message.Room)Protocol.Decode(Protocol.Encode(new Message.Room(lobby, [])))).Lobby);
            room.Members.Add(new Member(Lobby.Host, "Alice"));
            Assert.StartsWith("Cette partie reprend une campagne sauvegardée : elle attend Bob.", room.Join(1002, "Eve"));
            Assert.Null(room.Join(Bob, "Bob"));
            Assert.Equal(Bob, room.Lobby.Seats[1].Peer);
            // Nothing of the save changes: not a race, not a team; Bob only says he is ready.
            Assert.False(room.Apply(Bob, new LobbyRequest.SetRace(room.Lobby.Seats[1].Id, Faction.Nautolan)));
            Assert.False(room.Apply(Lobby.Host, new LobbyRequest.SetTeam(room.Lobby.Seats[2].Id, 2)));
            Assert.False(room.Apply(Lobby.Host, new LobbyRequest.AddBot()));
            Assert.Equal(["Un joueur n'est pas prêt."], room.Lobby.Problems());
            Assert.True(room.Apply(Bob, new LobbyRequest.SetReady(true)));
            Assert.Empty(room.Lobby.Problems());

            room.Lobby.Seat(saved, room.NameOf);
            Assert.Equal(Alice, saved.Player);
            Assert.Equal(g.Humans, saved.Humans);
            Assert.Null(saved.Factions[Bobs].AI);
            Assert.Equal(Difficulty.Hard, saved.Factions[Bot].AI!.Difficulty);

            // Bob leaves; the host gives his seat to a bot rather than wait.
            room.Leave(Bob);
            Assert.True(room.Apply(Lobby.Host, new LobbyRequest.SetKind(room.Lobby.Seats[1].Id, SeatKind.Bot, Difficulty.Easy)));
            Assert.Empty(room.Lobby.Problems());
            room.Lobby.Seat(saved, room.NameOf);
            Assert.Equal(["Alice"], saved.Humans.Values);
            Assert.Equal(Difficulty.Easy, saved.Factions[Bobs].AI!.Difficulty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Galaxy_snapshot_sizes_of_a_medium_and_a_large_galaxy()
    {
        foreach (var (size, seats) in new[] { (MapSize.Medium, 3), (MapSize.Large, 4), (MapSize.Large, 6) })
        {
            var g = Campaign(5, size, seats);
            PlayAhead(g, 900);
            // A snapshot carries the events since the previous one, not those of the whole game.
            g.Events.Clear();
            var bytes = Bytes(g, Bobs).Length;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 100; i++) Bytes(g, Bobs);
            var ms = watch.Elapsed.TotalMilliseconds / 100;
            var chart = GalaxySnapshot.Chart(g).Length;
            output.WriteLine($"{size}, {seats} camps, {g.Planets.Count} planètes, {g.Fleets.Count} flottes à 15 min : {bytes} octets (carte à part : {chart}), "
                + $"{bytes * Protocol.GalaxyRate / 1024:0.0} Kio/s à {Protocol.GalaxyRate} instantanés/s, {ms:0.000} ms d'écriture");
            // Well under what a battle snapshot of a duel weighs, and under ENet's MTU: never in fragments.
            Assert.True(bytes < 1392, $"{bytes} octets");
        }
    }
}
