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
    /// <summary>Continue the most recent saved campaign instead of starting a new one.</summary>
    public bool Resume { get; init; }
    /// <summary>The save to load instead, picked on the "Charger une partie" screen (its path).</summary>
    public string? Load { get; init; }
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
/// --intro (the opening, which a capture or a start into a screen skips), --music-dump=DIR (the music as WAV files, then quit),
/// --lang=fr|en (the language, whatever the options say) and --lang-report=FILE (each text shown without English, written there once),
/// --music-record=FILE.wav (the game's mixed sound saved as it quits; with --audio-driver Dummy before --, nothing is heard),
/// --sfx-dump=DIR (every sound effect as a WAV file, then quit), --colors=standard|adapted (the sides' colors, whatever the
/// options say), --update-url=URL (where the menu looks for a newer version, even in a capture: a local stand-in for GitHub),
/// --start=tutorial (the guided first battle), --start=replay with --replay=FILE (that replay played), --menu=replays and
/// --replays=DIR (the list of replays, kept in DIR rather than user://replays),
/// --profile=FILE (the player's profile kept in FILE: achievements and statistics count then even in a capture, never in user://),
/// --menu=home|new|controls|credits|options|lobby-MODE (the menu screen opened first; new: the
/// modes; lobby-skirmish, lobby-campaign...: that mode's lobby, see LobbyMode; skirmish: lobby-skirmish),
/// --seats=N and --teams=T,T... (that lobby's bots added up to N seats, then its teams in seat order),
/// --size=small|medium|large (the map, or the galaxy, of --start),
/// --terrain=none|light|dense (the terrain of --start's battle and of the stress scene, none without it; and of the dev runs' lobbies),
/// --mode=survival|zones|assault|regicide (the rules of --start=battle; in an assault, the player defends),
/// --wave=N (a survival game starts at wave N, the first a few seconds away: the late waves, or the end, without playing up to them),
/// --end=SECONDS (the battle played ahead, every side on the AI, until decided or that old, then its end screen: a campaign
/// battle's once decided; with --raid and --choose=command, a campaign alone reaches one).
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
    /// <summary>--wave=N: a survival game started from the command line begins at wave N.</summary>
    public static int Wave;
    /// <summary>--end=SECONDS: a battle played ahead until decided or that old, then its end screen.</summary>
    public static double End;
    public static float? Zoom;
    public static string? Shot;
    public static int Frames = 240;
    public static string? Dump;
    public static Godot.Vector2? At;
    public static string Menu = "home";
    public static MapSize Size = MapSize.Medium;
    /// <summary>--terrain=none|light|dense: the battle terrain of a dev run; null: none for --start, the lobby's own otherwise.</summary>
    public static TerrainDensity? Terrain;
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
    /// <summary>--deals: a campaign started with deals made and offered, the diplomacy list open, for its captures.</summary>
    public static bool Deals;
    /// <summary>--no-hud: the battle without its interface, for captures of what is on the field.</summary>
    public static bool NoHud;
    /// <summary>--veterans[=one]: a dev battle shows veterans and fleet orders: the player's first warships promoted, the fleet (or the best of it) selected on a patrol.</summary>
    public static string? Veterans;
    /// <summary>--lang=fr|en: the language of this run. --lang-report=FILE: the texts shown with no English, for the catalog (Lang).</summary>
    public static string? Lang, LangReport;
    public static double Speed, Truce, Leave;
    public static double[] Attack = [], Raid = [], Build = [];
    public static string[] Choose = [];
    public static string? Answer, Snap, SavePath;
    /// <summary>--replays=DIR: where replays are listed and kept, instead of user://replays; --replay=FILE: the replay --start=replay plays.</summary>
    public static string? Replays, Replay;
    /// <summary>--profile=FILE: the profile (achievements, statistics) read and written there; a capture then counts too.</summary>
    public static string? Profile;
    /// <summary>--music-dump=DIR: the music as WAV files there, then quit. --music-record=FILE.wav: the game's sound saved as it quits (music on in a capture).</summary>
    public static string? MusicDump, MusicRecord;
    /// <summary>--sfx-dump=DIR: the sound effects as WAV files there, then quit. --update-url=URL: where to look for a newer version.</summary>
    public static string? SfxDump, UpdateUrl;
    /// <summary>--colors=standard|adapted: the sides' colors for this run, the options left as they are.</summary>
    public static SideColors? Colors;
    /// <summary>--say=TEXT: a dev run writes this line in the chat once in the lobby.</summary>
    public static string? Say;
    /// <summary>--password=WORD: the game a dev run or a dedicated server hosts is locked with it; a dev run that joins answers with it.</summary>
    public static string? Password;
    /// <summary>--spectate: a dev run that joins (--join) comes only to watch.</summary>
    public static bool Spectate;
    /// <summary>--direct: through the online server, a dev run's game offers the direct link, and a dev run that joins accepts it.</summary>
    public static bool Direct;

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
                case "--wave": Wave = int.Parse(value); break;
                case "--end": End = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--zoom": Zoom = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--shot": Shot = value; break;
                case "--frames": Frames = int.Parse(value); break;
                case "--dump": Dump = value; break;
                case "--at" when value.Split(',') is [var ax, var ay]:
                    At = new Godot.Vector2(float.Parse(ax, System.Globalization.CultureInfo.InvariantCulture), float.Parse(ay, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--menu": Menu = value; break;
                case "--size": Size = Enum.Parse<MapSize>(value, true); break;
                case "--terrain": Terrain = Enum.Parse<TerrainDensity>(value, true); break;
                case "--seats": Seats = int.Parse(value); break;
                case "--teams": Teams = [.. value.Split(',').Select(int.Parse)]; break;
                case "--mode" when value == "campaign": Campaign = true; break;
                case "--mode": Mode = Enum.Parse<BattleMode>(value, true); break;
                case "--host": Host = true; break;
                case "--intro": Intro = true; break;
                case "--no-hud": NoHud = true; break;
                case "--veterans": Veterans = value == "one" ? "one" : "all"; break;
                case "--lang": Lang = value; break;
                case "--lang-report": LangReport = value; break;
                case "--music-dump": MusicDump = value; break;
                case "--sfx-dump": SfxDump = value; break;
                case "--update-url": UpdateUrl = value; break;
                case "--colors": Colors = Enum.Parse<SideColors>(value, true); break;
                case "--music-record": MusicRecord = value; break;
                case "--server": Server = true; break;
                case "--join": Join = value; break;
                case "--name": Name = value; break;
                case "--say": Say = value; break;
                case "--password": Password = value; break;
                case "--spectate": Spectate = true; break;
                case "--direct": Direct = true; break;
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
                case "--deals": Deals = true; break;
                case "--truce": Truce = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--answer": Answer = value; break;
                case "--save": SavePath = value; break;
                case "--replays": Replays = value; break;
                case "--profile": Profile = value; break;
                case "--replay": Replay = value; break;
                case "--resume": Resume = true; break;
                case "--watch": Watch = true; break;
                case "--leave": Leave = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--snap": Snap = value; break;
            }
        }
        if (Relay && !port) Port = Sim.Net.Relay.DefaultPort;
    }
}
