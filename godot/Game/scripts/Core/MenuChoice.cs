// What starts a galaxy campaign: a new one from the menu's lobby (or --start=campaign), or the saved one.
using SandboxShip.Battle;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Core;

public sealed record MenuChoice
{
    /// <summary>Without Seats: the player's race, on that race's own side.</summary>
    public Faction Player { get; init; } = Faction.Klaed;
    /// <summary>Without Seats: one or two opponents, every faction against all others.</summary>
    public IReadOnlyList<Faction> Enemies { get; init; } = [Faction.Nairan];
    /// <summary>Who plays, in seat order (Lobby.Slots): the first two get the capitals farthest apart.</summary>
    public IReadOnlyList<Slot> Seats { get; init; } = [];
    /// <summary>The local player's side among the seats; null: the first seat's.</summary>
    public Side? Me { get; init; }
    /// <summary>Continue the saved campaign instead of starting a new one.</summary>
    public bool Resume { get; init; }
    /// <summary>The galaxy of a new game: generated from the seed, or the hand-made classic one (two players).</summary>
    public MapKind Map { get; init; } = MapKind.Random;
    public int Seed { get; init; } = 1;
    public MapSize Size { get; init; } = MapSize.Medium;
    /// <summary>Without Seats, the enemies' AI; with --skip, the player's while the galaxy plays ahead.</summary>
    public Difficulty Difficulty { get; init; } = Difficulty.Normal;
    /// <summary>A campaign over the network: the host's galaxy, or a player's replica of it (Network.NetGalaxy); the rest is unused.</summary>
    public Network.NetGalaxy? Net { get; init; }

    /// <summary>Who plays: the seats, or else the player against the enemies, one side per race.</summary>
    public IReadOnlyList<Slot> Slots => Seats.Count > 0 ? Seats : BattleOptions.Versus(Player, Enemies, Difficulty);
}

/// <summary>
/// Launch options for checking a screen without clicking through the menu, after `--` on the
/// command line: --start=menu|battle|sandbox|campaign|gallery (every ship class and platform level, paused)|raid (a battle centered on the colossus)|stress
/// (three large fleets in a fight, one step a frame, frame times logged before a --shot run quits), --skip=SECONDS (battle or galaxy played
/// ahead, the player's side on the AI too), --zoom=Z, --shot=FILE.png and --frames=N (capture, then quit),
/// --intro (the opening, which a capture or a start into a screen skips),
/// --menu=home|new|controls|credits|options|lobby-MODE (the menu screen opened first; new: the
/// modes; lobby-skirmish, lobby-campaign...: that mode's lobby, see LobbyMode; skirmish: lobby-skirmish),
/// --seats=N and --teams=T,T... (that lobby's bots added up to N seats, then its teams in seat order),
/// --size=small|medium|large (the map, or the galaxy, of --start),
/// --mode=survival|zones|assault|regicide (the rules of --start=battle; in an assault, the player defends).
/// Network (Network.Session): --host (hosts a lobby of --mode, a skirmish by default, with a seat open to a player, and launches
/// once everyone is ready), --join=ADDRESS:PORT (joins it, and is ready once seated), --server (a dedicated server of --mode, with
/// Godot's --headless), --port=N, --name=X (the player's, or the server's game), --order=SECONDS (the player's ships attack-move to
/// the middle of the map once the battle is that old), --pause=SECONDS (then the player pauses the game for everyone).
/// Online server: --relay (runs it, with Godot's --headless, on --port, 27029 by default), --master=HOST:PORT (the online server the
/// game uses, instead of the options'), --online (with --host: hosts through the online server), --join=online (opens the multiplayer
/// screen, picks the first game the online server relays, and joins it), --public[=ADDRESS] (with --server: listed by the online server,
/// at ADDRESS or else where it sees the server from), --beacon=PORT (the local network's beacons, instead of 24681), --probe (measures
/// round trips directly on --port and through --master, then quits).
/// Campaign over the network (--host --mode=campaign, --join): --speed=N (the galaxy's speed, set by the host), --attack=SECONDS,...
/// (at those galaxy ages, the player's fleets leave for the nearest planet of another human), --raid=SECONDS,... (for the Errants'
/// planet), --choose=command|auto,... (the battle prompts answered in turn, the last answer repeated), --truce=SECONDS (a truce
/// offered to another human), --answer=yes|no (the truce offers answered), --save=FILE (where the host saves the campaign, even
/// with --shot; and what --resume reads), --resume (with --host: the saved campaign's lobby), --snap=DIR (captures at the key
/// moments: prompts, battles, their end, offers, the lobby; numbered in turn), --watch (a battle others command is watched when
/// offered), --leave=SECONDS (at that age of a campaign battle it commands, the player leaves it to the AI), --build=SECONDS,... (at those galaxy ages, frigates queued at every planet of the player, as credits allow), --pause=SECONDS (in the galaxy: at that galaxy age, the player pauses it for everyone, and sets it going 4 s later).
/// </summary>
public static class Dev
{
    public static string Start = "menu";
    public static double Skip;
    public static float? Zoom;
    public static string? Shot;
    public static int Frames = 240;
    public static string? Dump;
    public static Godot.Vector2? At;
    public static string Menu = "home";
    public static MapSize Size = MapSize.Medium;
    public static int Seats;
    public static int[] Teams = [];
    public static BattleMode Mode = BattleMode.Standard;
    public static bool Host, Server, Relay, Online, Probe;
    /// <summary>
    /// A headless service, with no player at this machine (--server, --relay, --probe): no window,
    /// no sprite forged, no warm-up, no sound, only the network.
    /// </summary>
    public static bool Service => Server || Relay || Probe;
    public static string? Join, Name, Master, Public;
    public static int Port = Sim.Net.Protocol.DefaultPort, Beacon = Sim.Net.Protocol.BeaconPort;
    public static double Order, Pause;
    public static bool Campaign, Resume, Watch;
    /// <summary>--intro: the opening plays even for a capture or a start straight into a screen.</summary>
    public static bool Intro;
    public static double Speed, Truce, Leave;
    public static double[] Attack = [], Raid = [], Build = [];
    public static string[] Choose = [];
    public static string? Answer, Snap, SavePath;

    static double[] Ages(string value) => [.. value.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))];

    public static void Parse(IEnumerable<string> args)
    {
        var port = false;
        foreach (var arg in args)
        {
            var (key, value) = arg.Split('=', 2) is [var k, var v] ? (k, v) : (arg, "");
            switch (key)
            {
                case "--start": Start = value; break;
                case "--skip": Skip = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--zoom": Zoom = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--shot": Shot = value; break;
                case "--frames": Frames = int.Parse(value); break;
                case "--dump": Dump = value; break;
                case "--at" when value.Split(',') is [var ax, var ay]:
                    At = new Godot.Vector2(float.Parse(ax, System.Globalization.CultureInfo.InvariantCulture), float.Parse(ay, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--menu": Menu = value; break;
                case "--size": Size = Enum.Parse<MapSize>(value, true); break;
                case "--seats": Seats = int.Parse(value); break;
                case "--teams": Teams = [.. value.Split(',').Select(int.Parse)]; break;
                case "--mode" when value == "campaign": Campaign = true; break;
                case "--mode": Mode = Enum.Parse<BattleMode>(value, true); break;
                case "--host": Host = true; break;
                case "--intro": Intro = true; break;
                case "--server": Server = true; break;
                case "--join": Join = value; break;
                case "--name": Name = value; break;
                case "--port": (Port, port) = (int.Parse(value), true); break;
                case "--relay": Relay = true; break;
                case "--master": Master = value; break;
                case "--online": Online = true; break;
                case "--public": Public = value; break;
                case "--beacon": Beacon = int.Parse(value); break;
                case "--probe": Probe = true; break;
                case "--order": Order = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--pause": Pause = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--speed": Speed = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--attack": Attack = Ages(value); break;
                case "--raid": Raid = Ages(value); break;
                case "--build": Build = Ages(value); break;
                case "--choose": Choose = value.Split(','); break;
                case "--truce": Truce = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--answer": Answer = value; break;
                case "--save": SavePath = value; break;
                case "--resume": Resume = true; break;
                case "--watch": Watch = true; break;
                case "--leave": Leave = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--snap": Snap = value; break;
            }
        }
        if (Relay && !port) Port = Sim.Net.Relay.DefaultPort;
    }
}
