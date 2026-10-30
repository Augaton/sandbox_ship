// A lobby shared over the network (not in the web version): players take open seats as they
// join and free them as they go; each changes its own seat only, the leader everything; a
// dedicated server's first player leads; nobody keeps a remote player without a seat.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class LobbyRoomTests
{
    const int Bob = 1001, Eve = 1002, Max = 1003;

    /// <summary>A hosted skirmish: the host, then two seats open to players.</summary>
    static LobbyRoom Hosted()
    {
        var l = Lobby.Create(LobbyMode.Skirmish, 5);
        l.SetKind(l.Seats[1], SeatKind.Open);
        l.AddBot();
        l.SetKind(l.Seats[2], SeatKind.Open);
        var room = new LobbyRoom(l);
        room.Members.Add(new Member(Lobby.Host, "Hôte"));
        return room;
    }

    static Seat SeatOf(LobbyRoom room, int peer) => room.Lobby.Seats.Single(s => s.Kind == SeatKind.Human && s.Peer == peer);

    [Fact]
    public void Players_take_the_open_seats_in_turn_until_the_game_is_full()
    {
        var room = Hosted();
        Assert.Null(room.Join(Bob, "Bob"));
        Assert.Null(room.Join(Eve, "  Bob\n "));
        Assert.Equal("La partie est complète.", room.Join(Max, "Max"));
        Assert.Equal("Déjà dans la partie.", room.Join(Bob, "Bob"));
        Assert.Equal(1, room.Lobby.Seats.IndexOf(SeatOf(room, Bob)));
        Assert.Equal(2, room.Lobby.Seats.IndexOf(SeatOf(room, Eve)));
        // Names come clean and unique.
        Assert.Equal(["Hôte", "Bob", "Bob 2"], room.Members.Select(m => m.Name));
        Assert.Equal("Joueur", LobbyRoom.CleanName(" \t"));
        Assert.Equal(LobbyRoom.MaxName, LobbyRoom.CleanName(new string('a', 50)).Length);
        // Each player has its side; the host keeps the first.
        Assert.Equal(new Side(0), room.Lobby.SideOfPeer(Lobby.Host));
        Assert.Equal(new Side(1), room.Lobby.SideOfPeer(Bob));
        Assert.Equal(new Side(2), room.Lobby.SideOfPeer(Eve));
        // One goes: its seat is open again, for the next one.
        room.Leave(Bob);
        Assert.Equal(SeatKind.Open, room.Lobby.Seats[1].Kind);
        Assert.Null(room.Join(Max, "Max"));
        Assert.Equal(1, room.Lobby.Seats.IndexOf(SeatOf(room, Max)));
    }

    [Fact]
    public void A_player_changes_its_own_seat_and_nothing_else()
    {
        var room = Hosted();
        room.Join(Bob, "Bob");
        room.Join(Eve, "Eve");
        var (host, bob, eve) = (room.Lobby.Seats[0], SeatOf(room, Bob), SeatOf(room, Eve));
        Assert.True(room.Apply(Bob, new LobbyRequest.SetRace(bob.Id, Faction.Nautolan)));
        Assert.True(room.Apply(Bob, new LobbyRequest.SetTeam(bob.Id, 2)));
        var free = Lobby.Colors.First(c => room.Lobby.ColorFree(bob, c) && c != bob.Color);
        Assert.True(room.Apply(Bob, new LobbyRequest.SetColor(bob.Id, free)));
        Assert.True(room.Apply(Bob, new LobbyRequest.SetReady(true)));
        Assert.Equal((Faction.Nautolan, 2, free, true), (bob.Race, bob.Team, bob.Color, bob.Ready));
        // Not another's seat, nor a color taken, nor the settings, the seats or their kinds.
        var before = System.Text.Json.JsonSerializer.Serialize(room.Lobby, Lobby.Json);
        Assert.False(room.Apply(Bob, new LobbyRequest.SetRace(eve.Id, Faction.Klaed)));
        Assert.False(room.Apply(Bob, new LobbyRequest.SetTeam(host.Id, 3)));
        Assert.False(room.Apply(Bob, new LobbyRequest.SetColor(bob.Id, eve.Color)));
        Assert.False(room.Apply(Bob, new LobbyRequest.SetKind(eve.Id, SeatKind.Bot)));
        Assert.False(room.Apply(Bob, new LobbyRequest.SetKind(bob.Id, SeatKind.Open)));
        Assert.False(room.Apply(Bob, new LobbyRequest.SetKind(host.Id, SeatKind.Human)));
        Assert.False(room.Apply(Bob, new LobbyRequest.AddBot()));
        Assert.False(room.Apply(Bob, new LobbyRequest.Remove(eve.Id)));
        Assert.False(room.Apply(Bob, new LobbyRequest.Settings(LobbySettings.Of(room.Lobby) with { Seed = 77 })));
        // Nor anyone who is not in the room.
        Assert.False(room.Apply(Max, new LobbyRequest.SetReady(true)));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(room.Lobby, Lobby.Json));
    }

    [Fact]
    public void A_player_moves_to_an_open_seat_and_nobody_is_ready_after_a_change_of_seat()
    {
        var room = Hosted();
        room.Lobby.AddBot();
        room.Lobby.SetKind(room.Lobby.Seats[3], SeatKind.Open);
        room.Join(Bob, "Bob");
        Assert.True(room.Apply(Bob, new LobbyRequest.SetReady(true)));
        var to = room.Lobby.Seats[3];
        Assert.True(room.Apply(Bob, new LobbyRequest.SetKind(to.Id, SeatKind.Human)));
        Assert.Equal(to, SeatOf(room, Bob));
        Assert.False(to.Ready);
        Assert.Equal(SeatKind.Open, room.Lobby.Seats[1].Kind);
        // A bot's seat is the leader's to give, not a player's to take.
        room.Lobby.SetKind(room.Lobby.Seats[1], SeatKind.Bot);
        Assert.False(room.Apply(Bob, new LobbyRequest.SetKind(room.Lobby.Seats[1].Id, SeatKind.Human)));
        // The host may sit there: its own seat opens.
        Assert.True(room.Apply(Lobby.Host, new LobbyRequest.SetKind(room.Lobby.Seats[1].Id, SeatKind.Human)));
        Assert.Equal(room.Lobby.Seats[1], SeatOf(room, Lobby.Host));
        Assert.Equal(SeatKind.Open, room.Lobby.Seats[0].Kind);
    }

    [Fact]
    public void The_host_changes_everything_and_a_player_moved_out_of_its_seat_must_go()
    {
        var room = Hosted();
        room.Join(Bob, "Bob");
        room.Join(Eve, "Eve");
        var bob = SeatOf(room, Bob);
        Assert.True(room.Apply(Lobby.Host, new LobbyRequest.SetRace(bob.Id, Faction.Nairan)));
        Assert.True(room.Apply(Lobby.Host, new LobbyRequest.Settings(LobbySettings.Of(room.Lobby) with { Seed = 77, Size = MapSize.Small, Fog = false })));
        Assert.Equal((77, MapSize.Small, false), (room.Lobby.Seed, room.Lobby.Size, room.Lobby.Fog));
        Assert.False(room.Apply(Lobby.Host, new LobbyRequest.Settings(LobbySettings.Of(room.Lobby) with { ZoneTarget = 123 })));
        Assert.Empty(room.Seatless());
        Assert.True(room.Apply(Lobby.Host, new LobbyRequest.SetKind(bob.Id, SeatKind.Bot, Difficulty.Hard)));
        Assert.Equal((SeatKind.Bot, Difficulty.Hard), (bob.Kind, bob.Difficulty));
        Assert.True(room.Apply(Lobby.Host, new LobbyRequest.Remove(SeatOf(room, Eve).Id)));
        Assert.Equal([Bob, Eve], room.Seatless());
        // A host may even leave its own seat, as in a game played alone: the lobby then says it needs one.
        Assert.True(room.Apply(Lobby.Host, new LobbyRequest.SetKind(room.Lobby.Seats[0].Id, SeatKind.Bot)));
        Assert.Contains("Il faut une place « Toi ».", room.Lobby.Problems());
    }

    [Fact]
    public void Launching_waits_for_every_remote_player_to_be_ready()
    {
        var room = Hosted();
        room.Lobby.SetKind(room.Lobby.Seats[2], SeatKind.Closed);
        Assert.Contains("Il faut au moins deux places occupées.", room.Lobby.Problems());
        room.Join(Bob, "Bob");
        Assert.Equal(["Un joueur n'est pas prêt."], room.Lobby.Problems());
        room.Apply(Bob, new LobbyRequest.SetReady(true));
        Assert.Empty(room.Lobby.Problems());
        room.Apply(Bob, new LobbyRequest.SetReady(false));
        Assert.Equal(["Un joueur n'est pas prêt."], room.Lobby.Problems());
    }

    [Fact]
    public void On_a_dedicated_server_the_first_player_in_leads_and_the_next_one_when_it_goes()
    {
        var l = Lobby.Create(LobbyMode.Skirmish, 5);
        l.Dedicated = true;
        foreach (var s in l.Seats) l.SetKind(s, SeatKind.Open);
        var room = new LobbyRoom(l);
        Assert.Equal(-1, room.Leader);
        room.Join(Bob, "Bob");
        room.Join(Eve, "Eve");
        Assert.Equal(Bob, room.Leader);
        // The leader has the host's rights, over the settings and the other seats.
        Assert.True(room.Apply(Bob, new LobbyRequest.AddBot()));
        Assert.True(room.Apply(Bob, new LobbyRequest.SetRace(SeatOf(room, Eve).Id, Faction.Nautolan)));
        Assert.False(room.Apply(Eve, new LobbyRequest.AddBot()));
        // But keeps a seat of its own.
        Assert.False(room.Apply(Bob, new LobbyRequest.SetKind(SeatOf(room, Bob).Id, SeatKind.Bot)));
        Assert.False(room.Apply(Bob, new LobbyRequest.Remove(SeatOf(room, Bob).Id)));
        // Nobody plays on the server: no "Toi" to ask for, only every player ready.
        Assert.Equal(["Un joueur n'est pas prêt."], room.Lobby.Problems());
        room.Apply(Bob, new LobbyRequest.SetReady(true));
        room.Apply(Eve, new LobbyRequest.SetReady(true));
        Assert.Empty(room.Lobby.Problems());
        room.Leave(Bob);
        Assert.Equal(Eve, room.Leader);
        Assert.True(room.Apply(Eve, new LobbyRequest.AddBot()));
    }

    [Fact]
    public void A_local_lobby_follows_the_same_rules_with_every_right()
    {
        var l = Lobby.Create(LobbyMode.Skirmish, 5);
        Assert.True(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.AddBot()));
        Assert.True(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.SetKind(l.Seats[2].Id, SeatKind.Bot, Difficulty.Easy)));
        Assert.Equal(Difficulty.Easy, l.Seats[2].Difficulty);
        // "Toi" on another seat: you move there.
        Assert.True(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.SetKind(l.Seats[1].Id, SeatKind.Human)));
        Assert.Equal(new Side(0), l.SideOfPeer());
        Assert.Equal(1, l.Seats.FindIndex(s => s.Kind == SeatKind.Human));
        Assert.Equal(SeatKind.Open, l.Seats[0].Kind);
        // Survival keeps everyone in one team.
        var survival = Lobby.Create(LobbyMode.Survival, 5);
        Assert.False(LobbyRules.Apply(survival, Lobby.Host, true, new LobbyRequest.SetTeam(survival.Seats[0].Id, 2)));
    }
}
