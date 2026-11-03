// Reading a galaxy snapshot into a replica (see GalaxySnapshot.cs for the layout): planets are
// updated in place, fleets, sides and battles by id, the rest replaced. Nothing of the
// simulation runs: the replica holds what the host said, and only what the viewer's team knows.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static partial class GalaxySnapshot
{
    /// <summary>
    /// The chart (see GalaxySnapshot.Chart) into a replica: its size, lanes and planets, which
    /// the snapshots then bring up to date. Of another version, or broken, it changes nothing.
    /// </summary>
    public static void ReadChart(BinaryReader r, Galaxy replica)
    {
        if (r.ReadUInt32() != ChartMagic) throw new InvalidDataException("Pas une carte de galaxie");
        var version = r.ReadUInt16();
        if (version != Version) throw new InvalidDataException($"Carte de galaxie en version {version}, version {Version} attendue");
        var (w, h) = (r.ReadSingle(), r.ReadSingle());
        var lanes = new List<Lane>();
        // Lanes come before the planets they join: checked once both are read.
        for (var n = Count(r); n > 0; n--) lanes.Add(new Lane(r.ReadVarint(), r.ReadVarint()));
        var planets = new List<Planet>();
        for (var n = Count(r); n > 0; n--)
        {
            planets.Add(new Planet
            {
                Id = planets.Count, Name = r.ReadText(), X = r.ReadSingle(), Y = r.ReadSingle(), Income = r.ReadSingle(), MineSlots = r.ReadVarint(),
            });
        }
        if (lanes.Exists(l => (uint)l.A >= (uint)planets.Count || (uint)l.B >= (uint)planets.Count)) throw new InvalidDataException("Voie vers une planète inconnue");
        (replica.W, replica.H, replica.Lanes, replica.Planets) = (w, h, lanes, planets);
    }

    /// <summary>
    /// Brings `replica` to the snapshot: the galaxy as its viewer's team knows it, the viewer as
    /// its Player, with the events the snapshot carries added to its Events; returns the host's
    /// clock. Null, and nothing changes, while the replica has no chart (it comes reliably, but
    /// on its own channel: a first snapshot may pass it). A snapshot of another version is
    /// refused before anything changes; one that does not hold together (an unknown value, a
    /// planet count other than the chart's) throws InvalidDataException.
    /// </summary>
    public static GalaxyClock? Read(BinaryReader r, Galaxy replica)
    {
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Pas un instantané de galaxie");
        var version = r.ReadUInt16();
        if (version != Version) throw new InvalidDataException($"Instantané de galaxie en version {version}, version {Version} attendue");
        if (replica.Planets.Count == 0) return null;
        var viewer = SideOf(r);
        replica.Player = viewer;
        replica.Time = r.ReadDouble();
        var clock = new GalaxyClock(r.ReadSingle(), false);
        var flags = r.ReadByte();
        clock = clock with { Paused = (flags & 1) != 0 };
        replica.Winner = (flags & 2) != 0 ? SideOf(r) : null;
        replica.Humans.Clear();
        for (var n = Count(r); n > 0; n--) replica.Humans[SideOf(r)] = r.ReadText();
        if (Count(r) != replica.Planets.Count) throw new InvalidDataException("Planètes autres que celles de la carte");
        foreach (var p in replica.Planets) ReadPlanet(r, replica, p);
        ReadFleets(r, replica);
        ReadSides(r, replica, viewer);
        ReadBattles(r, replica);
        replica.Truces = [];
        for (var n = Count(r); n > 0; n--) replica.Truces.Add(new Truce { A = SideOf(r), B = SideOf(r), Remaining = r.ReadSingle() });
        replica.Pacts = [];
        for (var n = Count(r); n > 0; n--) replica.Pacts.Add(new Pact { Kind = Valid<DealKind>(r.ReadByte()), A = SideOf(r), B = SideOf(r) });
        replica.Tributes = [];
        for (var n = Count(r); n > 0; n--) replica.Tributes.Add(new Tribute { Payer = SideOf(r), Payee = SideOf(r), PerMinute = r.ReadSingle(), Remaining = r.ReadSingle() });
        replica.Offers = [];
        for (var n = Count(r); n > 0; n--)
        {
            replica.Offers.Add(new DealOffer
            {
                From = SideOf(r), To = SideOf(r), Kind = Valid<DealKind>(r.ReadByte()), Amount = r.ReadSingle(), PerMinute = r.ReadBoolean(), Remaining = r.ReadSingle(),
            });
        }
        replica.Ongoing = [];
        for (var n = Count(r); n > 0; n--)
        {
            replica.Ongoing.Add(new Incident
            {
                Kind = Valid<IncidentKind>(r.ReadByte()), Planet = PlanetOf(r, replica), Other = OtherOf(r, replica), Remaining = r.ReadSingle(), Ships = ReadShips(r),
            });
        }
        ReadEvents(r, replica);
        return clock;
    }

    /// <summary>Events that came on the reliable channel (the `vital` stream of Write), added to the replica's Events.</summary>
    public static void ReadEvents(BinaryReader r, Galaxy replica)
    {
        for (var n = Count(r); n > 0; n--) replica.Events.Add(ReadEvent(r, replica));
    }

    /// <summary>A count, checked: a broken snapshot must not ask for a huge list.</summary>
    static int Count(BinaryReader r)
    {
        var n = r.ReadVarint();
        return n is >= 0 and <= 1 << 16 ? n : throw new InvalidDataException($"Liste de {n} éléments");
    }

    static Side SideOf(BinaryReader r) => r.ReadSide();

    /// <summary>A side as n + 1, 0 for none.</summary>
    static Side? OptionalSide(BinaryReader r)
    {
        var id = r.ReadVarint() - 1;
        return id is < -1 or > Lobby.MaxSeats ? throw new InvalidDataException($"Camp inconnu : {id}") : id >= 0 ? new Side(id) : null;
    }

    static int PlanetOf(BinaryReader r, Galaxy g) =>
        r.ReadVarint() is var id && (uint)id < (uint)g.Planets.Count ? id : throw new InvalidDataException($"Planète inconnue : {id}");

    /// <summary>An incident's other planet, as n + 1: -1 for none.</summary>
    static int OtherOf(BinaryReader r, Galaxy g) =>
        r.ReadVarint() - 1 is var id && id >= -1 && id < g.Planets.Count ? id : throw new InvalidDataException($"Planète inconnue : {id}");

    /// <summary>An enum value the host's build knows: the view looks them up, an unknown one would throw there.</summary>
    static T Valid<T>(byte value) where T : struct, Enum => Wire.Known<T>(value);

    static TechId? OptionalTech(BinaryReader r) => r.ReadByte() is var t && t != byte.MaxValue ? Valid<TechId>(t) : null;

    static HeroId ReadHero(BinaryReader r)
    {
        var h = new HeroId(Valid<Faction>(r.ReadByte()), Valid<HeroRole>(r.ReadByte()));
        return h.Role == default && h.Faction == default || Heroes.Defs.ContainsKey(h) ? h : throw new InvalidDataException($"Héros inconnu : {h}");
    }

    static ShipCounts ReadShips(BinaryReader r)
    {
        var ships = new ShipCounts();
        for (var n = Count(r); n > 0; n--) ships[Valid<ShipClass>(r.ReadByte())] = r.ReadVarint();
        return ships;
    }

    static List<HeroId> ReadHeroes(BinaryReader r)
    {
        var heroes = new List<HeroId>();
        for (var n = Count(r); n > 0; n--) heroes.Add(ReadHero(r));
        return heroes;
    }

    static void ReadPlanet(BinaryReader r, Galaxy g, Planet p)
    {
        p.Special = r.ReadByte() is var special && special != byte.MaxValue ? Valid<PlanetSpecial>(special) : null;
        var flags = r.ReadByte();
        p.Owner = (flags & Owned) != 0 ? SideOf(r) : null;
        // Out of sight, nothing is known of what guards it.
        (p.Station, p.Mines, p.Platforms, p.PlatformTier, p.Lab, p.RepairDock, p.IonCannon, p.StarLance, p.LanceCharge) = (0, 0, 0, 1, 0, 0, 0, 0, 0);
        p.Occupation = null;
        if ((flags & Seen) != 0)
        {
            p.Station = r.ReadVarint();
            p.Mines = r.ReadVarint();
            p.Platforms = r.ReadVarint();
            p.PlatformTier = r.ReadVarint();
            p.Lab = r.ReadVarint();
            p.RepairDock = r.ReadVarint();
            p.IonCannon = r.ReadVarint();
            p.StarLance = r.ReadVarint();
            if (p.StarLance > 0) p.LanceCharge = r.ReadSingle();
            if ((flags & Occupied) != 0) p.Occupation = new Occupation { Side = SideOf(r), Remaining = r.ReadSingle() };
        }
        p.Units = [];
        p.Buildings = [];
        p.Repeat = [];
        p.Rally = null;
        if ((flags & Own) == 0) return;
        p.Units = ReadQueue(r);
        p.Buildings = ReadQueue(r);
        for (var n = Count(r); n > 0; n--) p.Repeat.Add(Valid<ShipClass>(r.ReadByte()));
        if ((flags & Rallied) != 0) p.Rally = PlanetOf(r, g);
    }

    static List<GalaxyQueueItem> ReadQueue(BinaryReader r)
    {
        var queue = new List<GalaxyQueueItem>();
        for (var n = Count(r); n > 0; n--)
        {
            queue.Add(new GalaxyQueueItem
            {
                Kind = Valid<GalaxyBuildKind>(r.ReadByte()), Cls = Valid<ShipClass>(r.ReadByte()), Hero = ReadHero(r),
                Remaining = r.ReadSingle(), Total = r.ReadSingle(), Cost = r.ReadSingle(),
            });
        }
        return queue;
    }

    /// <summary>Fleets by id, in the host's order; those the snapshot no longer lists are gone from the replica.</summary>
    static void ReadFleets(BinaryReader r, Galaxy g)
    {
        var fleets = new OrderedDictionary<int, Fleet>();
        for (var n = Count(r); n > 0; n--)
        {
            var id = r.ReadVarint();
            if (!g.Fleets.TryGetValue(id, out var f)) f = new Fleet { Id = id };
            f.Side = SideOf(r);
            f.Ships = ReadShips(r);
            f.Heroes = ReadHeroes(r);
            var flags = r.ReadByte();
            f.At = (flags & Docked) != 0 ? PlanetOf(r, g) : null;
            f.From = PlanetOf(r, g);
            f.Came = f.From;
            (f.To, f.Progress) = (null, 0);
            if ((flags & Moving) != 0) (f.To, f.Progress) = (PlanetOf(r, g), r.ReadSingle());
            f.BattleId = (flags & Fighting) != 0 ? r.ReadVarint() : null;
            f.Condition = r.ReadSingle();
            f.Path = [];
            if ((flags & Routed) != 0) for (var k = Count(r); k > 0; k--) f.Path.Add(PlanetOf(r, g));
            fleets[id] = f;
        }
        g.Fleets = fleets;
    }

    static void ReadSides(BinaryReader r, Galaxy g, Side viewer)
    {
        var sides = new OrderedDictionary<Side, GalaxyFaction>();
        for (var n = Count(r); n > 0; n--)
        {
            var side = SideOf(r);
            if (!g.Factions.TryGetValue(side, out var f)) f = new GalaxyFaction { Id = side };
            f.Race = Valid<Faction>(r.ReadByte());
            f.Team = r.ReadVarint();
            f.Color = r.ReadUInt32();
            var flags = r.ReadByte();
            f.Eliminated = (flags & Out) != 0;
            f.AI = (flags & Bot) != 0 ? f.AI ?? new GalaxyAIState() : null;
            f.AI?.Grudges.Clear();
            if ((flags & Grudge) != 0)
            {
                var grudge = r.ReadSingle();
                if (f.AI is { } ai) ai.Grudges[viewer] = grudge;
            }
            if (f.AI is { } bot)
            {
                bot.Personality = Valid<Personality>(r.ReadByte());
                bot.Trust.Clear();
                bot.Trust[viewer] = r.ReadSingle();
            }
            // Only the viewer's own economy is known.
            f.Credits = 0;
            f.Research = new ResearchLevels();
            f.Researching = null;
            f.ResearchQueue = [];
            f.HeroCooldowns = [];
            if ((flags & Viewer) != 0)
            {
                f.Credits = r.ReadDouble();
                var techs = Count(r);
                for (var t = 0; t < techs; t++)
                {
                    var level = r.ReadVarint();
                    if (t < Techs.All.Count) f.Research[Techs.All[t]] = level;
                }
                if (r.ReadBoolean()) f.Researching = ReadJob(r);
                for (var k = Count(r); k > 0; k--) f.ResearchQueue.Add(ReadJob(r));
                for (var k = Count(r); k > 0; k--) f.HeroCooldowns[ReadHero(r)] = r.ReadSingle();
            }
            sides[side] = f;
        }
        if (!sides.ContainsKey(viewer)) throw new InvalidDataException("Le camp du joueur manque à l'instantané");
        g.Factions = sides;
    }

    static ResearchJob ReadJob(BinaryReader r) => new() { Tech = Valid<TechId>(r.ReadByte()), Remaining = r.ReadSingle(), Total = r.ReadSingle(), Cost = r.ReadSingle() };

    /// <summary>Battles by id. A replica's battle has an empty world: what its sides bring comes with it, for the viewer's own.</summary>
    static void ReadBattles(BinaryReader r, Galaxy g)
    {
        var battles = new List<GalaxyBattle>();
        for (var n = Count(r); n > 0; n--)
        {
            var id = r.ReadVarint();
            var b = g.Battles.Find(x => x.Id == id) ?? new GalaxyBattle { Id = id, World = new World() };
            b.PlanetId = PlanetOf(r, g);
            var flags = r.ReadByte();
            b.Lane = (flags & OnLane) != 0 ? new LaneSpot(PlanetOf(r, g), PlanetOf(r, g), r.ReadSingle()) : null;
            b.Attacker = SideOf(r);
            b.Defender = SideOf(r);
            b.Status = Valid<BattleStatus>(r.ReadByte());
            b.Choices = [];
            for (var k = Count(r); k > 0; k--) b.Choices[SideOf(r)] = Valid<BattleStatus>(r.ReadByte());
            b.Commanders = [];
            for (var k = Count(r); k > 0; k--) b.Commanders.Add(SideOf(r));
            b.Elapsed = r.ReadSingle();
            b.Forces = null;
            if ((flags & Forces) != 0)
            {
                b.Forces = [];
                foreach (var side in new[] { b.Attacker, b.Defender })
                {
                    var ships = ReadShips(r);
                    var heroes = ReadHeroes(r);
                    var strength = r.ReadSingle();
                    var d = new PlanetDefense
                    {
                        Station = r.ReadVarint(), Platforms = r.ReadVarint(), Mines = r.ReadVarint(),
                        RepairDock = r.ReadVarint(), IonCannon = r.ReadVarint(), PlatformTier = r.ReadVarint(),
                    };
                    b.Forces[side] = new BattleForce(ships, heroes, strength, d);
                }
            }
            battles.Add(b);
        }
        g.Battles = battles;
    }

    static LaneSpot? ReadLane(BinaryReader r, Galaxy g) => r.ReadBoolean() ? new LaneSpot(PlanetOf(r, g), PlanetOf(r, g), r.ReadSingle()) : null;

    static GalaxyEvent ReadEvent(BinaryReader r, Galaxy g) => (Tag)r.ReadByte() switch
    {
        Tag.BattleStarted => new BattleStarted(r.ReadVarint()),
        Tag.BattleEnded => new BattleEnded(PlanetOf(r, g), SideOf(r), SideOf(r), Valid<BattleResult>(r.ReadByte()), ReadLane(r, g)),
        Tag.Captured => new Captured(PlanetOf(r, g), SideOf(r), OptionalSide(r)),
        Tag.PlanetBuilt => new PlanetBuilt(PlanetOf(r, g), SideOf(r), r.ReadText()),
        Tag.TechResearched => new TechResearched(SideOf(r), Valid<TechId>(r.ReadByte())),
        Tag.BountyPaid => new BountyPaid(PlanetOf(r, g), SideOf(r), r.ReadDouble(), OptionalTech(r), ReadLane(r, g)),
        Tag.GalaxyVictory => new GalaxyVictory(SideOf(r), r.ReadVarint()),
        Tag.DealOffered => new DealOffered(SideOf(r), SideOf(r), Valid<DealKind>(r.ReadByte()), r.ReadDouble(), r.ReadBoolean()),
        Tag.DealRefused => new DealRefused(SideOf(r), SideOf(r), Valid<DealKind>(r.ReadByte()), r.ReadText()),
        Tag.PactSigned => new PactSigned(Valid<DealKind>(r.ReadByte()), SideOf(r), SideOf(r)),
        Tag.PactEnded => new PactEnded(Valid<DealKind>(r.ReadByte()), SideOf(r), SideOf(r), OptionalSide(r)),
        Tag.TributeSigned => new TributeSigned(SideOf(r), SideOf(r), r.ReadDouble(), r.ReadBoolean()),
        Tag.TributeEnded => new TributeEnded(SideOf(r), SideOf(r), OptionalSide(r)),
        Tag.TruceSigned => new TruceSigned(SideOf(r), SideOf(r)),
        Tag.TruceEnded => new TruceEnded(SideOf(r), SideOf(r), OptionalSide(r)),
        Tag.IncidentStarted => new IncidentStarted(Valid<IncidentKind>(r.ReadByte()), PlanetOf(r, g), OtherOf(r, g), OptionalSide(r)),
        Tag.IncidentEnded => new IncidentEnded(Valid<IncidentKind>(r.ReadByte()), PlanetOf(r, g), OtherOf(r, g), OptionalSide(r)),
        Tag.RuinsExplored => new RuinsExplored(PlanetOf(r, g), SideOf(r), r.ReadDouble(), OptionalTech(r)),
        Tag.LanceFired => new LanceFired(PlanetOf(r, g), PlanetOf(r, g), SideOf(r)),
        var tag => throw new InvalidDataException($"Événement de galaxie inconnu : {tag}"),
    };
}
