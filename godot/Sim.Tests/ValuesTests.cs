// Values that come from the network, checked where they are read (not in the web version): a
// host may send any byte for a race, a class, a side, a type… One that names nothing used to get
// through the reading and bring the screen down where a dictionary is asked for it. A message
// that reads now holds only values the game knows; one that does not is refused as broken.
using System.Collections;
using System.Reflection;
using System.Text;
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class ValuesTests
{
    static byte[] Write(World w, Side viewer, List<SimEvent>? events = null)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true)) Snapshot.Write(w, viewer, bw, events ?? []);
        return ms.ToArray();
    }

    static void Refused(byte[] bytes) =>
        Assert.Throws<InvalidDataException>(() => Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), new World()));

    /// <summary>A small skirmish without fog, so that everything goes in the snapshot, a Kla'ed frigate in it.</summary>
    static World Small()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], Seed = 4, Size = MapSize.Small });
        w.Fog = false;
        Worlds.SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 400, 400);
        Steps.Run(w, 2);
        return w;
    }

    [Fact]
    public void A_host_that_sends_an_unknown_race_side_type_or_hero_is_refused()
    {
        var w = Small();
        var ship = w.Entities.First(e => e.Kind == EntityKind.Ship && e.Side == Faction.Klaed);
        var race = ship.Race;
        ship.Race = (Faction)42;
        Refused(Write(w, Faction.Klaed));
        ship.Race = race;

        // A seat's side that the snapshot does not list, and one beyond any the game numbers.
        ship.Side = new Side(5);
        Refused(Write(w, Faction.Klaed));
        ship.Side = new Side(9);
        Refused(Write(w, Faction.Klaed));
        ship.Side = Faction.Klaed;

        // A ship whose type is a station, a structure whose type is a ship.
        var type = ship.Type;
        ship.Type = "station";
        Refused(Write(w, Faction.Klaed));
        ship.Type = type;
        var station = Worlds.StationOf(w, Faction.Klaed)!;
        station.Type = Ids.Of(ShipClass.Frigate);
        Refused(Write(w, Faction.Klaed));
        station.Type = "station";

        ship.Hero = new HeroId(Faction.Klaed, (HeroRole)9);
        Refused(Write(w, Faction.Klaed));
        ship.Hero = null;

        // Untouched, it reads.
        Snapshot.Read(new BinaryReader(new MemoryStream(Write(w, Faction.Klaed))), new World());
    }

    [Fact]
    public void A_host_that_sends_an_unknown_weapon_strike_or_event_value_is_refused()
    {
        var w = Small();
        w.Projectiles.Add(new Projectile { Id = w.NextId++, Side = Faction.Nairan, Kind = (WeaponKind)77, X = 100, Y = 100, Speed = 1 });
        Refused(Write(w, Faction.Klaed));
        w.Projectiles.Clear();

        w.Strikes.Add(new Strike { Kind = (StrikeKind)50, Side = Faction.Nairan, X = 1, Y = 1, Radius = 10, Delay = 1, Total = 1 });
        Refused(Write(w, Faction.Klaed));
        w.Strikes.Clear();

        Refused(Write(w, Faction.Klaed, [new Hit(1, 1, (WeaponKind)99, 1, Faction.Nairan, null)]));
        // A research is told to its own side's team only.
        Refused(Write(w, Faction.Klaed, [new Researched(Faction.Klaed, (TechId)99)]));
        Refused(Write(w, Faction.Klaed, [new Victory(new Side(12), 3)]));
        Snapshot.Read(new BinaryReader(new MemoryStream(Write(w, Faction.Klaed, [new Researched(Faction.Klaed, TechId.Weapons)]))), new World());
    }

    [Fact]
    public void A_lobby_request_or_a_command_with_an_unknown_value_is_refused_as_it_is_read()
    {
        byte[] Request(LobbyRequest q) => Protocol.Encode(new Message.Ask(q));
        var bytes = Request(new LobbyRequest.SetRace(1, Faction.Nairan));
        bytes[^1] = 42;
        Assert.Throws<InvalidDataException>(() => Protocol.Decode(bytes));

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true)) Commands.Write(bw, new Command.EnqueueShip(ShipClass.Frigate, 1));
        var command = ms.ToArray();
        command[1] = 200;
        Assert.Throws<InvalidDataException>(() => Commands.ReadCommand(new BinaryReader(new MemoryStream(command))));
    }

    [Fact]
    public void A_side_that_never_joined_passes_for_the_errants_rather_than_for_no_race()
    {
        Assert.Equal(Faction.Nautolan, Worlds.RaceOf(new World(), Faction.Nautolan));
        Assert.Equal(Faction.Errant, Worlds.RaceOf(new World(), new Side(6)));
        Assert.Equal(Faction.Errant, Galaxies.RaceOf(new Galaxy(), new Side(5)));
    }

    // Every input of FuzzTests, read again: whatever reads holds only known values.

    [Fact]
    public void A_battle_snapshot_that_reads_holds_only_values_the_game_knows()
    {
        var w = FuzzTests.Battle(5);
        var whole = Write(w, Faction.Klaed, w.Events.ToList());
        var parts = Snapshot.WriteParts(w, Faction.Klaed, w.Events.ToList(), null, new SnapshotFrames());
        var read = 0;
        foreach (var input in FuzzTests.Mutations(whole, 0).Prepend(whole))
        {
            var replica = new World();
            if (!Reads(() => Snapshot.Read(new BinaryReader(new MemoryStream(input)), replica))) continue;
            Known(replica, "instantané");
            read++;
        }
        var salt = 0;
        foreach (var part in parts.Take(4))
        {
            foreach (var input in FuzzTests.Mutations(part, salt++))
            {
                var replica = new World();
                if (!Reads(() => Snapshot.ReadPart(new BinaryReader(new MemoryStream(input)), replica, new SnapshotFrames()))) continue;
                Known(replica, "partie d'instantané");
                read++;
            }
        }
        Assert.True(read > 0);
    }

    [Fact]
    public void A_galaxy_snapshot_that_reads_holds_only_values_the_game_knows()
    {
        var g = FuzzTests.Campaign(5);
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true)) GalaxySnapshot.Write(g, Faction.Klaed, bw, g.Events.ToList(), null, default);
        var snapshot = ms.ToArray();
        var chart = GalaxySnapshot.Chart(g);
        foreach (var input in FuzzTests.Mutations(snapshot, 0).Prepend(snapshot))
        {
            var replica = new Galaxy();
            GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(chart)), replica);
            if (!Reads(() => GalaxySnapshot.Read(new BinaryReader(new MemoryStream(input)), replica))) continue;
            Walk(replica, "galaxie");
        }
    }

    [Fact]
    public void A_message_or_a_command_that_reads_holds_only_values_the_game_knows()
    {
        var salt = 0;
        foreach (var seed in ProtocolTests.Every().Select(Protocol.Encode))
        {
            foreach (var input in FuzzTests.Mutations(seed, salt++).Prepend(seed))
            {
                Message? m = null;
                if (Reads(() => m = Protocol.Decode(input))) Walk(m!, "message");
            }
        }
    }

    /// <summary>Whether reading went through; a refusal of broken bytes is the other fine way.</summary>
    static bool Reads(Action read)
    {
        try
        {
            read();
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            return false;
        }
    }

    /// <summary>A battle as read: every value known, every entity's type fit for its kind, its side listed or a race's own.</summary>
    static void Known(World w, string what)
    {
        Walk(w, what);
        foreach (var e in w.Entities)
        {
            var fits = e.Kind switch
            {
                EntityKind.Ship => Ids.IsShip(e.Type),
                EntityKind.Hardpoint => Ids.IsRole(e.Type),
                _ => e.Type == "station" || Ids.IsBuildable(e.Type, out _),
            };
            Assert.True(fits, $"{what} : {e.Kind} de type {e.Type}");
            Assert.True(e.Side.Id <= (int)Faction.Errant || w.Factions.ContainsKey(e.Side), $"{what} : camp {e.Side} non listé");
        }
    }

    /// <summary>
    /// Walks what was read, fields and collections, and fails on an enum value its type does not
    /// have (flags aside), a side the game does not number, or a hero that is not one of its.
    /// </summary>
    static void Walk(object root, string what)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Visit(object? o, string path, int depth)
        {
            if (o is null || depth > 14) return;
            var t = o.GetType();
            if (t.IsEnum)
            {
                if (t.GetCustomAttribute<FlagsAttribute>() is null) Assert.True(Enum.IsDefined(t, o), $"{what} : {path} = {o} ({t.Name})");
                return;
            }
            switch (o)
            {
                case Side s:
                    Assert.True(s.Id is >= 0 and <= Lobby.MaxSeats, $"{what} : {path} = camp {s.Id}");
                    return;
                case HeroId h:
                    Assert.True(h == default || Heroes.Defs.ContainsKey(h), $"{what} : {path} = héros {h}");
                    return;
                case string or Delegate or Type or MemberInfo:
                    return;
            }
            if (t.IsPrimitive || t.IsPointer || o is decimal) return;
            if (!t.IsValueType && !seen.Add(o)) return;
            if (o is IDictionary d)
            {
                foreach (DictionaryEntry kv in d)
                {
                    Visit(kv.Key, $"{path}[clé]", depth + 1);
                    Visit(kv.Value, $"{path}[{kv.Key}]", depth + 1);
                }
                return;
            }
            if (o is IEnumerable list)
            {
                var i = 0;
                foreach (var x in list) Visit(x, $"{path}[{i++}]", depth + 1);
                return;
            }
            if (t.Namespace?.StartsWith("SandboxShip", StringComparison.Ordinal) != true) return;
            for (var type = t; type is not null && type != typeof(object); type = type.BaseType)
            {
                foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    Visit(f.GetValue(o), $"{path}.{f.Name}", depth + 1);
                }
            }
        }
        Visit(root, "", 0);
    }
}
