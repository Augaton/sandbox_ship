// A campaign that lasts (not in the web version): free planets held by independents who fight
// for them, capitals that hold out, and a peace at the start between the great factions. None of
// it in a galaxy made without it (CampaignSetup.None, the tests' default), nor in older saves.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>
/// How a new campaign starts. `Independents`: every free planet is held by a garrison of its own
/// (Galaxies.Independent). `Fortified`: each capital starts with a level 2 station and two
/// platforms, which rise again while its faction holds it. `OpeningPeace`: galaxy seconds no great
/// faction may attack another, 0 for none.
/// </summary>
public sealed record CampaignSetup(bool Independents, bool Fortified, double OpeningPeace)
{
    /// <summary>The galaxy as it was: free planets for the taking, bare capitals, war at once.</summary>
    public static readonly CampaignSetup None = new(false, false, 0);

    /// <summary>The lobby's choices for the opening peace, in minutes; the first is its default.</summary>
    public static readonly IReadOnlyList<int> PeaceMinutes = [10, 0, 5, 15];

    /// <summary>A new campaign's: independents, fortified capitals, and the peace chosen in the lobby.</summary>
    public static CampaignSetup Of(int peaceMinutes) => new(true, true, Math.Clamp(peaceMinutes, 0, 60) * 60);
}

/// <summary>The opening peace is over: the great factions may attack each other.</summary>
public sealed record PeaceOver : GalaxyEvent;

public sealed partial class Planet
{
    /// <summary>
    /// The faction whose capital it was at the start of a campaign with fortified capitals: its
    /// platforms rise again, up to CapitalPlatforms, while that faction holds it. Null otherwise.
    /// </summary>
    public Side? Home;
    /// <summary>Galaxy seconds the next platform of a fortified capital has been rising for.</summary>
    public double Raising;
}

public static partial class Galaxies
{
    /// <summary>
    /// The independents: no galaxy faction, like the Errants, but holding every free planet of a
    /// campaign set up so, each with a garrison that never leaves it. Past the last seat's side.
    /// </summary>
    public static readonly Side Independent = new(Lobby.MaxSide);
    /// <summary>The independents' grey, on the map and in battle.</summary>
    public const uint IndependentColor = 0x9aa6ba;

    /// <summary>How a battle names a side: its race, or the independents of a campaign battle, whose ships are their planet's people's.</summary>
    public static string SideName(World world, Side side) => side == Independent ? "Indépendants" : Factions.Defs[Worlds.RaceOf(world, side)].Name;
    /// <summary>Level of a fortified capital's station at the start, and the platforms that rise again there.</summary>
    public const int CapitalStation = 2, CapitalPlatforms = 2;
    /// <summary>Galaxy seconds a fortified capital takes to raise a lost platform again.</summary>
    public const double RaiseTime = 90;

    /// <summary>Whether a side is a galaxy faction's: neither the Errants nor the independents, who never build, move or count for victory.</summary>
    public static bool IsFaction(Galaxy g, Side side) => g.Factions.ContainsKey(side);

    /// <summary>
    /// The race whose ships an independent planet flies, and whose look its garrison has in
    /// battle: one of the three peoples, from the planet's place in the galaxy, the same for every
    /// player and every replay.
    /// </summary>
    public static Faction IndependentRace(Planet p) => (Faction)(p.Id % 3);

    /// <summary>
    /// What guards a free planet, worth more the richer and the farther from the capitals it is:
    /// next door to one, a starting fleet takes the poorest; at the far end of the galaxy, a war
    /// fleet is needed. One or two platforms, then a battlecruiser for the wealthiest, frigates,
    /// and a support ship once there are a few. No draw: the classic galaxy stays mirrored.
    /// </summary>
    public static (int Platforms, ShipCounts Ships) Garrison(Planet p, int hops)
    {
        var worth = (700 + 250 * p.Income) * (0.6 + 0.25 * Math.Clamp(hops, 1, 5)) * p.Special switch
        {
            PlanetSpecial.Ruins => 1.5,
            PlanetSpecial.Rich => 1.3,
            PlanetSpecial.Shipyard or PlanetSpecial.Fortress or PlanetSpecial.Archive => 1.2,
            PlanetSpecial.Relay => 1.1,
            _ => 1.0,
        };
        var platforms = Math.Clamp((int)Math.Round(worth / 1500), 1, 2) + (p.Special == PlanetSpecial.Fortress ? 1 : 0);
        var left = worth - platforms * Buildables.Defs[BuildableType.Platform].Cost;
        var ships = new ShipCounts();
        var cruiser = Data.Ships.Defs[ShipClass.Battlecruiser].Cost;
        if (left >= cruiser * 2)
        {
            ships[ShipClass.Battlecruiser] = 1;
            left -= cruiser;
        }
        var frigate = Data.Ships.Defs[ShipClass.Frigate].Cost;
        var frigates = Math.Max(1, (int)Math.Floor(left / frigate));
        ships[ShipClass.Frigate] = frigates;
        if (frigates >= 3) ships[ShipClass.Support] = 1;
        return (platforms, ships);
    }

    /// <summary>
    /// A new campaign's setup, once its factions and the Errants have their planets: the
    /// independents take every free planet, each capital is fortified, and every two great
    /// factions of different teams start at peace.
    /// </summary>
    static void Settle(Galaxy g, IReadOnlyList<int> capitals, CampaignSetup setup)
    {
        if (setup.Independents)
        {
            var hops = Hops(g, capitals);
            foreach (var p in g.Planets)
            {
                if (p.Owner is not null) continue;
                var (platforms, ships) = Garrison(p, hops.GetValueOrDefault(p.Id, 5));
                p.Owner = Independent;
                p.Platforms = platforms;
                AddShipsAt(g, Independent, p.Id, ships);
            }
        }
        if (setup.Fortified)
        {
            foreach (var c in capitals)
            {
                var p = g.Planets[c];
                p.Station = Math.Max(p.Station, CapitalStation);
                p.Platforms = Math.Max(p.Platforms, CapitalPlatforms);
                p.Home = p.Owner;
            }
        }
        if (setup.OpeningPeace > 0)
        {
            var sides = g.Factions.Keys.ToList();
            for (var i = 0; i < sides.Count; i++)
            {
                for (var j = i + 1; j < sides.Count; j++)
                {
                    if (!Teammates(g, sides[i], sides[j])) g.Truces.Add(new Truce { A = sides[i], B = sides[j], Remaining = setup.OpeningPeace, Opening = true });
                }
            }
        }
    }

    /// <summary>
    /// A fortified capital its faction still holds, with a station, raises a lost platform again,
    /// one at a time, never during a battle there. Without a station, platforms hold nothing.
    /// </summary>
    static void RaiseCapitals(Galaxy g, double dt)
    {
        foreach (var p in g.Planets)
        {
            if (p.Home is not Side home || p.Owner != home || p.Station == 0 || p.Platforms >= CapitalPlatforms || BattleAt(g, p.Id))
            {
                p.Raising = 0;
                continue;
            }
            if ((p.Raising += dt) < RaiseTime) continue;
            p.Raising = 0;
            p.Platforms++;
        }
    }
}
