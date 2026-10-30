// The lobby (not in the web version): seats, their sides and colors, each mode's defaults, the
// reasons a game cannot start, and what it reads back from JSON.
using System.Text.Json;
using SandboxShip.Sim.Data;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class LobbyTests
{
    static Lobby Full(LobbyMode mode)
    {
        var lobby = Lobby.Create(mode, 7);
        while (lobby.AddBot() is not null) { }
        return lobby;
    }

    [Fact]
    public void The_seats_that_play_take_sides_0_to_2_then_4_and_up()
    {
        Assert.Equal([0, 1, 2, 4, 5, 6], Enumerable.Range(0, 6).Select(i => Lobby.SideOf(i).Id));
        var lobby = Full(LobbyMode.Skirmish);
        Assert.Equal(Lobby.MaxSeats, lobby.Seats.Count);
        Assert.Null(lobby.AddBot());
        // A closed seat and an open one do not play: the others close up, in order.
        lobby.SetKind(lobby.Seats[1], SeatKind.Closed);
        lobby.SetKind(lobby.Seats[3], SeatKind.Open);
        var slots = lobby.Slots();
        Assert.Equal([0, 1, 2, 4], slots.Select(s => s.Side.Id));
        Assert.Equal(new[] { 0, 2, 4, 5 }.Select(i => lobby.Seats[i].Color), slots.Select(s => s.Color!.Value));
        Assert.Equal(new Side(0), lobby.SideOfPeer());
        // The skirmish is made from them: each side in its seat's color, the raid on the Errants' side.
        lobby.SetKind(lobby.Seats[1], SeatKind.Bot);
        lobby.SetKind(lobby.Seats[3], SeatKind.Bot);
        var world = Scenario.CreateSkirmish(lobby.Skirmish());
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], world.Factions.Keys.Select(s => s.Id).Order());
        foreach (var (slot, seat) in lobby.Slots().Zip(lobby.Seats)) Assert.Equal(seat.Color, world.Factions[slot.Side].Color);
    }

    [Fact]
    public void The_player_can_sit_anywhere_and_the_galaxy_takes_six()
    {
        var lobby = Full(LobbyMode.Campaign);
        lobby.SetKind(lobby.Seats[0], SeatKind.Bot);
        lobby.SetKind(lobby.Seats[4], SeatKind.Human);
        Assert.Empty(lobby.Problems());
        Assert.Equal(new Side(5), lobby.SideOfPeer());
        var g = Galaxies.CreateGalaxy(lobby.Slots(), lobby.Seed, lobby.Layout());
        Assert.Equal(6, g.Factions.Count);
        Assert.Null(g.Factions[new Side(5)].AI);
        Assert.Equal(5, g.Factions.Values.Count(f => f.AI is not null));
        Assert.Equal(lobby.Seats.Select(s => s.Color).Order(), g.Factions.Values.Select(f => f.Color).Order());
    }

    [Fact]
    public void Colors_stay_unique_and_follow_the_race_when_they_can()
    {
        var lobby = Full(LobbyMode.Skirmish);
        Assert.Equal(lobby.Seats.Count, lobby.Seats.Select(s => s.Color).Distinct().Count());
        // Two of each race: the first of each wears its race's color.
        Assert.Equal(Factions.All.Select(f => Factions.Defs[f].Color), lobby.Seats.Take(3).Select(s => s.Color));
        Assert.False(lobby.SetColor(lobby.Seats[1], lobby.Seats[0].Color));
        Assert.False(lobby.SetColor(lobby.Seats[1], Factions.Defs[Faction.Errant].Color));
        // The Nairan seat turns Nautolan: green is taken, it keeps blue; once green is free, a seat that turns Nautolan in blue takes it.
        lobby.SetRace(lobby.Seats[1], Faction.Nautolan);
        Assert.Equal(Factions.Defs[Faction.Nairan].Color, lobby.Seats[1].Color);
        Assert.True(lobby.SetColor(lobby.Seats[2], Factions.PlayerColors.First(c => lobby.ColorFree(lobby.Seats[2], c))));
        lobby.SetRace(lobby.Seats[1], Faction.Nairan);
        lobby.SetRace(lobby.Seats[1], Faction.Nautolan);
        Assert.Equal(Factions.Defs[Faction.Nautolan].Color, lobby.Seats[1].Color);
        // A seat in a player color keeps it whatever its race.
        var violet = lobby.Seats[3].Color;
        lobby.SetRace(lobby.Seats[3], Faction.Nairan);
        Assert.Equal(violet, lobby.Seats[3].Color);
        Assert.Equal(lobby.Seats.Count, lobby.Seats.Select(s => s.Color).Distinct().Count());
    }

    [Fact]
    public void Each_mode_starts_as_it_plays()
    {
        foreach (var mode in Enum.GetValues<LobbyMode>())
        {
            var lobby = Lobby.Create(mode, 3);
            Assert.Equal([SeatKind.Human, SeatKind.Bot], lobby.Seats.Select(s => s.Kind));
            Assert.Empty(lobby.Problems());
        }
        // Survival: everyone in one team, whatever is asked.
        var survival = Full(LobbyMode.Survival);
        survival.SetTeam(survival.Seats[2], 3);
        Assert.All(survival.Seats, s => Assert.Equal(1, s.Team));
        Assert.True(survival.TeamsLocked);
        // Assault: you defend, the bot attacks; then the smaller team gets the next seat.
        var assault = Lobby.Create(LobbyMode.Assault, 3);
        Assert.Equal([1, 2], assault.Seats.Select(s => s.Team));
        assault.AddBot();
        assault.AddBot();
        Assert.Equal([1, 2, 1, 2], assault.Seats.Select(s => s.Team));
        assault.SetTeam(assault.Seats[0], 0);
        Assert.Equal(1, assault.Seats[0].Team);
        // The others start alone, at war with everyone; the raid and the fog only where they apply.
        var skirmish = Lobby.Create(LobbyMode.Skirmish, 3);
        Assert.All(skirmish.Seats, s => Assert.Equal(0, s.Team));
        Assert.Equal((true, true, BattleMode.Standard), (skirmish.Skirmish().Raid, skirmish.Skirmish().Fog, skirmish.Rules));
        Assert.Equal((false, true), (Lobby.Create(LobbyMode.Survival, 3).Skirmish().Raid, Lobby.Create(LobbyMode.Survival, 3).Skirmish().Fog));
        Assert.False(Lobby.Create(LobbyMode.Sandbox, 3).Skirmish().Fog);
        // Bots keep their difficulty, the sandbox's too: its panel turns their AI on at it.
        var sandbox = Lobby.Create(LobbyMode.Sandbox, 3);
        sandbox.Seats[1].Difficulty = Difficulty.Hard;
        Assert.Equal([null, Difficulty.Hard], sandbox.Slots().Select(s => s.AI));
        var zones = Lobby.Create(LobbyMode.Zones, 3);
        zones.ZoneTarget = 200;
        Assert.Equal((BattleMode.Zones, 200), (zones.Skirmish().Mode, zones.Skirmish().ZoneTarget));
    }

    [Fact]
    public void The_classic_map_is_for_two_only()
    {
        var lobby = Lobby.Create(LobbyMode.Campaign, 3);
        lobby.Map = MapKind.Classic;
        lobby.Fit();
        Assert.Same(GalaxyGen.Classic, lobby.Layout());
        lobby.AddBot();
        Assert.Equal(MapKind.Random, lobby.Map);
        Assert.Equal(3, lobby.Layout().Capitals.Count);
    }

    [Fact]
    public void A_game_that_cannot_start_says_why()
    {
        var lobby = Lobby.Create(LobbyMode.Skirmish, 3);
        lobby.SetKind(lobby.Seats[1], SeatKind.Human);
        Assert.Equal(["Une seule place peut être « Toi »."], lobby.Problems());
        lobby.SetKind(lobby.Seats[0], SeatKind.Bot);
        lobby.SetKind(lobby.Seats[1], SeatKind.Closed);
        Assert.Equal(["Il faut une place « Toi ».", "Il faut au moins deux places occupées."], lobby.Problems());

        // Everyone in one team: nobody to fight. Two teams, or one team against a lone seat: a game.
        var teams = Full(LobbyMode.Skirmish);
        foreach (var s in teams.Seats) teams.SetTeam(s, 2);
        Assert.Equal(["Tout le monde est dans la même équipe : il faut au moins deux camps ennemis."], teams.Problems());
        teams.SetTeam(teams.Seats[5], 0);
        Assert.Empty(teams.Problems());
        teams.SetTeam(teams.Seats[5], 3);
        Assert.Empty(teams.Problems());

        // Survival is played together, even alone.
        var survival = Lobby.Create(LobbyMode.Survival, 3);
        Assert.Empty(survival.Problems());
        survival.Remove(survival.Seats[1]);
        Assert.Empty(survival.Problems());

        var assault = Lobby.Create(LobbyMode.Assault, 3);
        assault.SetTeam(assault.Seats[1], 1);
        Assert.Equal(["L'assaut demande au moins un attaquant (équipe 2)."], assault.Problems());

        // A player who joined from afar has to be ready.
        var remote = Lobby.Create(LobbyMode.Regicide, 3);
        remote.SetKind(remote.Seats[1], SeatKind.Human, peer: 2);
        Assert.Equal(["Un joueur n'est pas prêt."], remote.Problems());
        remote.Seats[1].Ready = true;
        Assert.Empty(remote.Problems());
        Assert.Equal(new Side(1), remote.SideOfPeer(2));
    }

    [Fact]
    public void Seat_ids_stay_with_their_seats()
    {
        var lobby = Full(LobbyMode.Skirmish);
        var ids = lobby.Seats.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        lobby.Remove(lobby.Seats[2]);
        Assert.Equal(ids.Where((_, i) => i != 2), lobby.Seats.Select(s => s.Id));
        Assert.Equal(ids.Max() + 1, lobby.AddBot()!.Id);
        // The last seat stays.
        while (lobby.Remove(lobby.Seats[^1])) { }
        Assert.Single(lobby.Seats);
    }

    [Fact]
    public void A_lobby_reads_back_from_json_and_a_broken_one_is_refused()
    {
        var lobby = Full(LobbyMode.Assault);
        lobby.AssaultMinutes = 20;
        lobby.SetKind(lobby.Seats[3], SeatKind.Open);
        var text = JsonSerializer.Serialize(new List<Lobby> { lobby }, Lobby.Json);
        Assert.Contains("\"race\":\"nautolan\"", text);
        Assert.DoesNotContain("plays", text);
        var back = JsonSerializer.Deserialize<List<Lobby>>(text, Lobby.Json)![0];
        Assert.True(back.Sane());
        Assert.Equal(text, JsonSerializer.Serialize(new List<Lobby> { back }, Lobby.Json));
        Assert.Equal(lobby.Slots(), back.Slots());

        Lobby Read(Func<string, string> edit) => JsonSerializer.Deserialize<Lobby>(edit(JsonSerializer.Serialize(lobby, Lobby.Json)), Lobby.Json)!;
        var color = lobby.Seats[1].Color.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.False(Read(t => t.Replace(color, lobby.Seats[0].Color.ToString(System.Globalization.CultureInfo.InvariantCulture))).Sane());
        Assert.False(Read(t => t.Replace("\"team\":2", "\"team\":9")).Sane());
        Assert.False(Read(t => t.Replace("\"assaultMinutes\":20", "\"assaultMinutes\":3")).Sane());
        Assert.False(Read(t => t.Replace("\"nextId\":6", "\"nextId\":2")).Sane());
        Assert.False(Read(t => t.Replace("\"seats\":[", "\"seats\":[null,")).Sane());
        Assert.Throws<JsonException>(() => Read(t => t.Replace("\"nautolan\"", "\"errant2\"")));
    }
}
