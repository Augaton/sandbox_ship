// The campaign tutorial's galaxy (Game/scripts/Ui/CampaignTutorial.cs), small and fixed: the
// classic one, the player Kla'ed at Vesper against a Nairan AI at the far end. A peace of an hour
// between the two keeps the rival from attacking, no incident strikes, and under the lowest fleet
// cap the player's fleet, bigger than it, leaves a reserve in its first battle. The rival is easy
// and defensive, and the player's two neighbours well guarded: a balanced AI swept the galaxy to
// them in four minutes, ten with their garrisons; a defensive one, not in half an hour. It is
// never saved (Galaxy.Tutorial): the player's campaign stays as it was. Not in the web version.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static class TutorialGalaxy
{
    public const Faction Race = Faction.Klaed, Rival = Faction.Nairan;
    /// <summary>
    /// Planets of the classic galaxy (GalaxyMap): the player's capital, Vesper, and its two
    /// neighbours, both independent, Orin to take by arms and Maru to win over.
    /// </summary>
    public const int Capital = 0, Target = 1, Courted = 2;
    /// <summary>Enough to produce, research and win Maru over without waiting.</summary>
    public const double Credits = 20000;
    /// <summary>Galaxy seconds of peace between the great factions: longer than the tutorial takes.</summary>
    public const double Peace = 60 * 60;
    /// <summary>The fleet cap of its battles: the lowest, 150 points.</summary>
    public const FleetCap Cap = FleetCap.Low;

    /// <summary>
    /// What waits at the capital besides the two frigates and two scouts every capital has: 170 points
    /// in all, past the cap, the ships of the line and the craft a player meets first.
    /// </summary>
    public static ShipCounts Fleet() => new()
    {
        [ShipClass.Battlecruiser] = 10, [ShipClass.Frigate] = 36, [ShipClass.Corvette] = 24, [ShipClass.Scout] = 18,
        [ShipClass.Support] = 3, [ShipClass.Artillery] = 2,
    };

    /// <summary>
    /// What guards each of the player's neighbours besides their own garrison: Orin stands long
    /// enough for the first ships to fall and free room for the reserve, and the rival leaves both alone.
    /// </summary>
    public static ShipCounts Garrison() => new()
    {
        [ShipClass.Battlecruiser] = 1, [ShipClass.Frigate] = 6, [ShipClass.Corvette] = 4, [ShipClass.Support] = 1,
    };

    public static Galaxy Create()
    {
        var g = Galaxies.CreateGalaxy([new Slot(Race, Race), new Slot(Rival, Rival, 0, Difficulty.Easy, Personality: Personality.Defensive)], seed: 1, layout: GalaxyGen.Classic,
            incidents: false, setup: new CampaignSetup(Independents: true, Fortified: true, OpeningPeace: Peace, Cap));
        g.Tutorial = true;
        g.Factions[Race].Credits = Credits;
        // Research needs a laboratory.
        g.Planets[Capital].Lab = 1;
        Galaxies.AddShipsAt(g, Race, Capital, Fleet());
        foreach (var p in new[] { Target, Courted })
        {
            g.Planets[p].Platforms = 2;
            Galaxies.AddShipsAt(g, Galaxies.Independent, p, Garrison());
        }
        return g;
    }
}
