// The network's messages (not in the web version): each reads back to the very bytes it was
// written to, a broken one is refused with the expected exceptions, a player of another
// version is told why, and the local network's beacons read back or are ignored.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class ProtocolTests
{
    static Lobby SomeLobby()
    {
        var l = Lobby.Create(LobbyMode.Zones, 4242);
        l.AddBot();
        l.SetKind(l.Seats[1], SeatKind.Human, 77);
        l.Seats[1].Ready = true;
        l.SetKind(l.Seats[2], SeatKind.Open);
        l.ZoneTarget = 200;
        return l;
    }

    /// <summary>One of each message, as the network carries them (FuzzTests breaks them).</summary>
    internal static IEnumerable<Message> Every()
    {
        yield return Protocol.Hello("0.3.0", "Aurélien");
        yield return Protocol.Hello("0.3.0", "Zoé", true);
        yield return new Message.Welcome(123456789, "Partie d'Aurélien");
        yield return new Message.Bye("La partie est complète.");
        yield return new Message.Room(SomeLobby(), [new Member(Lobby.Host, "Hôte"), new Member(77, "Bob")]);
        yield return new Message.Ask(new LobbyRequest.SetKind(2, SeatKind.Bot, Difficulty.Hard));
        yield return new Message.Ask(new LobbyRequest.SetRace(1, Faction.Nautolan));
        yield return new Message.Ask(new LobbyRequest.SetTeam(1, 2));
        yield return new Message.Ask(new LobbyRequest.SetColor(1, Lobby.Colors[5]));
        yield return new Message.Ask(new LobbyRequest.SetReady(true));
        yield return new Message.Ask(new LobbyRequest.AddBot());
        yield return new Message.Ask(new LobbyRequest.Remove(3));
        yield return new Message.Ask(new LobbyRequest.SetPersonality(2, Personality.Technological));
        yield return new Message.Ask(new LobbyRequest.SetPersonality(2, null));
        yield return new Message.Ask(new LobbyRequest.Settings(new LobbySettings(MapKind.Random, MapSize.Large, 99, false, true, 50, 20, false, true,
            TerrainDensity.Dense, 10000, 70)));
        yield return new Message.Launch();
        yield return new Message.Start(SomeLobby());
        yield return new Message.Order(new Command.Move([4, 8, 15], 1200.5, 800.25, true));
        yield return new Message.Order(new Command.UseAbility(16, AbilityId.Cloak, new Aim(1, 2, 23)));
        yield return new Message.Order(new Command.Patrol([4, 8], [new Point(10, 20), new Point(30.5, 40)], true));
        yield return new Message.Order(new Command.SetStance([15], Stance.Passive));
        yield return new Message.Order(new Command.Guard([16], 23, true));
        yield return new Message.State([1, 2, 3, 250, 0]);
        yield return new Message.Ack(123456);
        yield return new Message.Chart([71, 67, 72, 84, 2, 0]);
        yield return new Message.Refused("Cible hors de vue");
        yield return new Message.Pause(true, "Bob");
        yield return new Message.Notice("Bob a quitté la partie : l'IA prend sa place.");
        yield return new Message.Back();
        yield return new Message.Say("On attaque au nord ?");
        yield return new Message.Said("Bob", new string('é', Chat.MaxText));
        yield return new Message.Record(BattleHistory.Bytes(FuzzTests.Battle(3).History!));
        yield return new Message.Order(new Command.Attack([4, 8], 23, Queued: true));
        foreach (var c in GalaxyCommands()) yield return new Message.Plan(c);
    }

    /// <summary>One galaxy command of each kind, the deals' included.</summary>
    internal static IEnumerable<GalaxyCommand> GalaxyCommands()
    {
        yield return new GalaxyCommand.SendFleet(3, 7, new ShipCounts { [ShipClass.Frigate] = 2 }, [new HeroId(Faction.Klaed, HeroRole.Admiral)]);
        yield return new GalaxyCommand.RedirectFleet(3, 8);
        yield return new GalaxyCommand.StopFleet(3);
        yield return new GalaxyCommand.EnqueueBuild(2, GalaxyBuild.Ship(ShipClass.Battlecruiser));
        yield return new GalaxyCommand.CancelBuild(2, 1, QueueSide.Buildings);
        yield return new GalaxyCommand.SetLoop(2, ShipClass.Fighter, true);
        yield return new GalaxyCommand.SetRally(2, 5);
        yield return new GalaxyCommand.StartTech(TechId.Shields);
        yield return new GalaxyCommand.CancelTech(0);
        yield return new GalaxyCommand.FireLance(2, 9);
        yield return new GalaxyCommand.OfferTruce(Faction.Nairan);
        yield return new GalaxyCommand.AnswerTruce(Faction.Nairan, true);
        yield return new GalaxyCommand.BreakTruce(Faction.Nairan);
        yield return new GalaxyCommand.ProposeDeal(Faction.Nautolan, DealKind.Tribute, 250, true);
        yield return new GalaxyCommand.AnswerDeal(Faction.Nautolan, DealKind.Alliance, false);
        yield return new GalaxyCommand.EndDeal(Faction.Nautolan, DealKind.Trade);
        yield return new GalaxyCommand.ChooseBattle(4, BattleStatus.Tactical);
        yield return new GalaxyCommand.DelegateBattle(4);
        yield return new GalaxyCommand.Retreat(4);
    }

    [Fact]
    public void Every_message_reads_back_to_the_bytes_it_was_written_to()
    {
        var tags = new HashSet<byte>();
        foreach (var m in Every())
        {
            var bytes = Protocol.Encode(m);
            var back = Protocol.Decode(bytes);
            Assert.Equal(m.GetType(), back.GetType());
            Assert.Equal(bytes, Protocol.Encode(back));
            tags.Add(bytes[0]);
        }
        Assert.Equal(19, tags.Count);
        // Fields come back as sent, not only their bytes.
        var room = (Message.Room)Protocol.Decode(Protocol.Encode(Every().OfType<Message.Room>().First()));
        Assert.Equal([new Member(Lobby.Host, "Hôte"), new Member(77, "Bob")], room.Members);
        Assert.Equal((SeatKind.Human, 77, true), (room.Lobby.Seats[1].Kind, room.Lobby.Seats[1].Peer, room.Lobby.Seats[1].Ready));
        Assert.Equal(200, room.Lobby.ZoneTarget);
        var ask = (Message.Ask)Protocol.Decode(Protocol.Encode(Every().OfType<Message.Ask>().First(a => a.Request is LobbyRequest.Settings)));
        Assert.Equal((10000, 70), (((LobbyRequest.Settings)ask.Request).Values.Credits, ((LobbyRequest.Settings)ask.Request).Values.WaveStrength));
        var hello = (Message.Hello)Protocol.Decode(Protocol.Encode(Protocol.Hello("0.3.0", "Aurélien")));
        Assert.Equal(Protocol.Hello("0.3.0", "Aurélien"), hello);
    }

    [Fact]
    public void A_broken_message_is_refused_and_never_half_read()
    {
        foreach (var m in Every())
        {
            var bytes = Protocol.Encode(m);
            // A snapshot's, a chart's or a record's bytes are their reader's to check; cut anywhere else, a message does not read.
            if (m is Message.State or Message.Chart or Message.Record) continue;
            for (var cut = 0; cut < bytes.Length; cut++)
            {
                var part = bytes[..cut];
                var e = Record.Exception(() => Protocol.Decode(part));
                Assert.True(e is InvalidDataException or EndOfStreamException, $"{m.GetType().Name} cut at {cut}: {e?.GetType().Name}");
            }
            Assert.Throws<InvalidDataException>(() => Protocol.Decode([.. bytes, 0]));
        }
        Assert.Throws<InvalidDataException>(() => Protocol.Decode([200]));
        // A text longer than any name or reason.
        Assert.Throws<InvalidDataException>(() => Protocol.Decode(Protocol.Encode(new Message.Bye(new string('x', 500)))));
        // A lobby that does not hold together: two seats of one color.
        var bad = SomeLobby();
        bad.Seats[1].Color = bad.Seats[0].Color;
        Assert.Throws<InvalidDataException>(() => Protocol.Decode(Protocol.Encode(new Message.Start(bad))));
    }

    [Fact]
    public void A_player_of_another_version_is_told_why()
    {
        Assert.Null(Protocol.Mismatch(Protocol.Hello("0.3.0", "Bob"), "0.3.0"));
        Assert.Equal("Version différente : la partie est en 0.3.0, ton jeu en 0.2.0.", Protocol.Mismatch(Protocol.Hello("0.2.0", "Bob"), "0.3.0"));
        var older = Protocol.Hello("0.3.0", "Bob") with { Snapshots = Snapshot.Version - 1 };
        Assert.StartsWith("Version du jeu incompatible avec l'hôte", Protocol.Mismatch(older, "0.3.0"));
    }

    [Fact]
    public void Beacons_read_back_and_anything_else_on_their_port_is_ignored()
    {
        var b = new LanGame(Protocol.Version, "0.3.0", "Partie de Bob", LobbyMode.Assault, 2, 4, 24690);
        Assert.Equal(b, Protocol.DecodeLanGame(Protocol.Encode(b)));
        Assert.Null(Protocol.DecodeLanGame([]));
        Assert.Null(Protocol.DecodeLanGame([1, 2, 3, 4, 5, 6]));
        var bytes = Protocol.Encode(b);
        Assert.Null(Protocol.DecodeLanGame(bytes[..^3]));
        // The mode: before taken, seats, port, the dedicated flag and the lock.
        bytes[^7] = 200;
        Assert.Null(Protocol.DecodeLanGame(bytes));
    }
}
