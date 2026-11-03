// AI personalities (not in the web version): four ways for an AI to play, in battle and in the
// campaign, beside Balanced, the AI as it always was. Each one tilts the same few choices the AI
// makes anyway; none is meant to be stronger than the others, only to play differently (the
// series of AI against AI that check it are in PLAN.md).
namespace SandboxShip.Sim.Data;

public enum Personality { Balanced, Aggressive, Defensive, Economic, Technological }

/// <summary>
/// What a personality changes. In battle: the wave the AI waits for (× the usual) and how fast it
/// grows after each, when it goes all in, how long after an assault on its home it strikes back
/// (0: never); how many platforms it keeps by station level and the
/// odds of one more; how many outposts and the odds of one more; the odds of a lab and of a
/// research; the share of its income set aside for the next station level; how far its fleet
/// leans to small, medium and large hulls (× the usual weights); the army and the odds before
/// it builds its star lance. In the campaign: the edge an attack waits for (×), and how it leans
/// in diplomacy (1 as Balanced, less to refuse, more to seek): truces, alliances, trade, and
/// demanding tribute from the weaker.
/// </summary>
public sealed record PersonalityDef(
    string Name, string Description,
    double Wave, double WaveGrowth, double AllInTime, double Counter,
    int Platforms, double PlatformOdds, int Outposts, double OutpostOdds, double LabOdds, double ResearchOdds,
    double SaveShare, double Small, double Medium, double Large, int LanceFleet, double LanceOdds,
    double Edge, double Peace, double Friendship, double Trade, double Extortion)
{
    /// <summary>How far the fleet leans to a hull size.</summary>
    public double Lean(SizeClass size) => size switch
    {
        SizeClass.Small => Small,
        SizeClass.Medium => Medium,
        _ => Large,
    };
}

public static class Personalities
{
    public static readonly IReadOnlyList<Personality> All = Enum.GetValues<Personality>();

    /// <summary>The four a random draw picks from: Balanced is the AI of the tests and of an AI set up by hand.</summary>
    public static readonly IReadOnlyList<Personality> Drawn = [Personality.Aggressive, Personality.Defensive, Personality.Economic, Personality.Technological];

    public static readonly IReadOnlyDictionary<Personality, PersonalityDef> Defs = new Dictionary<Personality, PersonalityDef>
    {
        [Personality.Balanced] = new("Équilibrée", "Un peu de tout, selon ce qu'elle voit de l'ennemi.",
            Wave: 1, WaveGrowth: 1.2, AllInTime: 1800, Counter: 0, Platforms: 1, PlatformOdds: 0.25, Outposts: 2, OutpostOdds: 0.15, LabOdds: 0.15, ResearchOdds: 0.2,
            SaveShare: 0.5, Small: 1, Medium: 1, Large: 1, LanceFleet: 8, LanceOdds: 0.25,
            Edge: 1, Peace: 1, Friendship: 1, Trade: 1, Extortion: 1),
        [Personality.Aggressive] = new("Agressive", "Attaque tôt et souvent avec des vaisseaux légers, se défend peu, cherche peu la paix.",
            Wave: 0.6, WaveGrowth: 1.1, AllInTime: 1200, Counter: 0, Platforms: 0, PlatformOdds: 0.1, Outposts: 1, OutpostOdds: 0.1, LabOdds: 0.06, ResearchOdds: 0.08,
            SaveShare: 0.3, Small: 1.6, Medium: 1.2, Large: 0.6, LanceFleet: 8, LanceOdds: 0.15,
            Edge: 0.85, Peace: 0.4, Friendship: 0.5, Trade: 0.7, Extortion: 2),
        [Personality.Defensive] = new("Défensive", "Se retranche derrière ses plateformes, puis contre-attaque une fois l'assaut repoussé.",
            Wave: 1.5, WaveGrowth: 1.25, AllInTime: 2400, Counter: 45, Platforms: 3, PlatformOdds: 0.6, Outposts: 2, OutpostOdds: 0.1, LabOdds: 0.2, ResearchOdds: 0.25,
            SaveShare: 0.6, Small: 0.8, Medium: 1.2, Large: 1.2, LanceFleet: 8, LanceOdds: 0.3,
            Edge: 1.25, Peace: 1.6, Friendship: 1.5, Trade: 1.2, Extortion: 0.3),
        [Personality.Economic] = new("Économe", "S'étend et mine d'abord, puis frappe avec une grosse armée.",
            Wave: 1.6, WaveGrowth: 1.15, AllInTime: 1800, Counter: 0, Platforms: 1, PlatformOdds: 0.2, Outposts: 4, OutpostOdds: 0.5, LabOdds: 0.15, ResearchOdds: 0.15,
            SaveShare: 0.8, Small: 1, Medium: 1, Large: 1.1, LanceFleet: 8, LanceOdds: 0.25,
            Edge: 1.1, Peace: 1.3, Friendship: 1.2, Trade: 2, Extortion: 0.6),
        [Personality.Technological] = new("Technologique", "Recherche sans relâche et mise sur les gros vaisseaux.",
            Wave: 1.2, WaveGrowth: 1.2, AllInTime: 2100, Counter: 0, Platforms: 1, PlatformOdds: 0.2, Outposts: 2, OutpostOdds: 0.15, LabOdds: 0.6, ResearchOdds: 0.6,
            SaveShare: 0.6, Small: 0.6, Medium: 0.9, Large: 1.8, LanceFleet: 5, LanceOdds: 0.5,
            Edge: 1.05, Peace: 1.2, Friendship: 1.4, Trade: 1.5, Extortion: 0.6),
    };

    /// <summary>
    /// The personality a seat set to random plays, drawn from the game's seed and the seat: the
    /// same for every machine and every replay of that game, without touching the world's draws.
    /// </summary>
    public static Personality Draw(int seed, int seat)
    {
        var h = unchecked((uint)seed * 2654435761u ^ (uint)(seat + 1) * 40503u);
        h ^= h >> 15;
        h = unchecked(h * 2246822519u);
        h ^= h >> 13;
        return Drawn[(int)(h % (uint)Drawn.Count)];
    }
}
