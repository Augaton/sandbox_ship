// Spectators, and players back in a game they left (not in the web version): a spectator has no
// seat, never leads, gives no order and sees the battle through the eyes it picks, every side's
// without fog by default; a player who left finds its seat kept under its name, takes its side
// back from the AI, and is served again from a whole snapshot.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class SpectatorsTests
{
    const int Bob = 1001, Sam = 1003;
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    static LobbyRoom Room()
    {
        var l = Lobby.Create(LobbyMode.Skirmish, 7);
        l.Dedicated = true;
        foreach (var s in l.Seats) l.SetKind(s, SeatKind.Open);
        return new LobbyRoom(l);
    }

    [Fact]
    public void A_spectator_has_no_seat_never_leads_and_comes_only_if_the_host_takes_them()
    {
        var room = Room();
        Assert.Null(room.Watch(Sam, "Sam"));
        Assert.True(room.Watches(Sam));
        Assert.Equal(-1, room.Leader);
        Assert.DoesNotContain(room.Lobby.Seats, s => s.Kind == SeatKind.Human);
        Assert.Null(room.Join(Bob, "Bob"));
        Assert.Equal(Bob, room.Leader);
        // Having no seat, a spectator is never sent off for want of one.
        Assert.Empty(room.Seatless());
        Assert.Equal("Déjà dans la partie.", room.Watch(Sam, "Sam"));
        for (var i = 0; i < LobbyRoom.MaxSpectators - 1; i++) Assert.Null(room.Watch(2000 + i, "Sam"));
        Assert.Equal("Il y a déjà assez de spectateurs.", room.Watch(3000, "Zoé"));
        var closed = Room();
        closed.Lobby.Spectators = false;
        Assert.Equal("L'hôte ne prend pas de spectateurs.", closed.Watch(Sam, "Sam"));
    }

    [Fact]
    public void A_player_back_takes_the_seat_kept_for_them_and_nobody_else_does()
    {
        var room = Room();
        Assert.Null(room.Join(Bob, "Bob"));
        var seat = room.Lobby.Seats.Find(s => s.Peer == Bob && s.Kind == SeatKind.Human)!.Id;
        room.Leave(Bob);
        Assert.Null(room.Rejoin(4004, "Bob", seat));
        Assert.Equal(4004, room.Lobby.Seats.Find(s => s.Id == seat)!.Peer);
        Assert.Equal("Ta place n'est plus libre.", room.Rejoin(4005, "Ève", seat));
    }

    [Fact]
    public void Spectators_their_view_and_the_settings_that_let_them_in_read_back()
    {
        var l = Lobby.Create(LobbyMode.Skirmish, 3);
        var room = (Message.Room)Protocol.Decode(Protocol.Encode(new Message.Room(l, [new Member(Lobby.Host, "Hôte"), new Member(Sam, "Sam", true)])));
        Assert.True(room.Members[1].Watches);
        Assert.False(room.Members[0].Watches);
        var settings = new LobbySettings(MapKind.Random, MapSize.Small, 5, true, true, 100, 15, false);
        Assert.Equal(settings, ((LobbyRequest.Settings)((Message.Ask)Protocol.Decode(Protocol.Encode(new Message.Ask(new LobbyRequest.Settings(settings))))).Request).Values);
        Assert.True(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.Settings(settings)));
        Assert.False(l.Spectators);
        Assert.Equal(-1, ((Message.Spectate)Protocol.Decode(Protocol.Encode(new Message.Spectate(-1)))).Side);
        Assert.Equal(2, ((Message.Spectate)Protocol.Decode(Protocol.Encode(new Message.Spectate(2)))).Side);
    }

    /// <summary>The host plays Kla'ed, Bob the Nairan from afar; Sam watches. A fog over a small map of three.</summary>
    sealed class Table
    {
        public readonly World World = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Slots = [new Slot(Klaed, Faction.Klaed), new Slot(Nairan, Faction.Nairan), new Slot(Nautolan, Faction.Nautolan)], Seed = 3, Size = MapSize.Small,
        });
        public readonly BattleHost Host;
        public readonly Dictionary<int, World> Replicas = [];
        public readonly Dictionary<int, SnapshotFrames> Frames = [];
        public readonly List<string> Refused = [];

        public Table()
        {
            Host = new BattleHost(World, Deliver);
            Follow(Bob);
            Follow(Sam);
            Host.Add(Bob, Nairan);
            Host.Add(Sam, Klaed, watches: true);
            Host.View(Sam, Klaed, true);
        }

        /// <summary>A fresh replica for a peer, as one who just connected has.</summary>
        public void Follow(int peer) => (Replicas[peer], Frames[peer]) = (new World(), new SnapshotFrames());

        void Deliver(int peer, Message m)
        {
            switch (Protocol.Decode(Protocol.Encode(m)))
            {
                case Message.State s:
                    // Against a baseline this peer never read, a part does not read: the test fails here.
                    var part = Snapshot.ReadPart(new BinaryReader(new MemoryStream(s.Snapshot)), Replicas[peer], Frames[peer]);
                    Assert.True(part.Read);
                    if (part.Completed > 0) Host.Acked(peer, part.Completed);
                    break;
                case Message.Refused r: Refused.Add(r.Reason); break;
            }
        }

        public void Run(double seconds)
        {
            for (var i = Js.Round(seconds / Steps.Step); i > 0; i--)
            {
                Host.BeforeStep();
                Steps.StepWorld(World);
                Host.Collect();
                World.Events.Clear();
                Host.Tick(Steps.Step);
            }
        }
    }

    [Fact]
    public void A_spectator_sees_everything_without_fog_or_one_sides_view_and_gives_no_order()
    {
        var t = new Table();
        Assert.True(t.World.Fog);
        t.Run(0.3);
        var far = StationOf(t.World, Nautolan)!.Id;
        Assert.False(t.Replicas[Sam].Fog);
        Assert.True(t.Replicas[Sam].Entities.Has(far));
        Assert.False(t.Replicas[Bob].Entities.Has(far));
        // The host's own world keeps its fog: it went only for the time of the spectator's snapshot.
        Assert.True(t.World.Fog);
        Assert.Equal([Bob], t.Host.Players);
        var ship = t.World.Entities.First(e => e.Side == Klaed && e.Kind == EntityKind.Ship || e.Side == Klaed && e.Kind == EntityKind.Structure);
        t.Host.Receive(Sam, new Command.Stop([ship.Id]));
        t.Run(0.1);
        Assert.Contains("Tu regardes la bataille", t.Refused);
        // Through Nairan eyes now: the fog is back, and the far station out of sight.
        t.Host.View(Sam, Nairan, false);
        t.Follow(Sam);
        t.Run(0.3);
        Assert.True(t.Replicas[Sam].Fog);
        Assert.False(t.Replicas[Sam].Entities.Has(far));
    }

    [Fact]
    public void A_player_back_takes_its_side_from_the_ai_and_reads_a_whole_snapshot()
    {
        var t = new Table();
        t.Run(0.3);
        t.Host.Drop(Bob);
        Assert.NotNull(t.World.Factions[Nairan].AI);
        t.Run(0.3);
        // A new connection, a new replica: its first snapshot must go whole.
        t.Follow(Bob);
        t.Host.Return(Bob, Nairan);
        Assert.Null(t.World.Factions[Nairan].AI);
        t.Run(0.3);
        Assert.True(t.Replicas[Bob].Entities.Has(StationOf(t.World, Nairan)!.Id));
    }

    [Fact]
    public void A_campaign_player_back_is_a_human_again_and_gets_the_chart_first()
    {
        var g = Galaxies.CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false);
        g.Humans[Nairan] = "Bob";
        g.Factions[Nairan].AI = null;
        var sent = new List<Message>();
        var host = new GalaxyHost(g, (_, m) => sent.Add(m));
        host.Add(Bob, Nairan);
        host.Drop(Bob);
        Assert.False(g.Humans.ContainsKey(Nairan));
        Assert.NotNull(g.Factions[Nairan].AI);
        host.Return(Bob, Nairan, "Bob");
        Assert.Equal("Bob", g.Humans[Nairan]);
        Assert.Null(g.Factions[Nairan].AI);
        host.Tick(1);
        Assert.IsType<Message.Chart>(sent.First(m => m is Message.Chart or Message.Map));
    }
}
