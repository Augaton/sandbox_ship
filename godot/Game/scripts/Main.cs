// Root of the game: loads the sprites, then shows one screen at a time (menu, battle, galaxy).
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip;

public partial class Main : Node
{
    public static Main Instance { get; private set; } = null!;

    Node? screen;
    Ui.Loading loading = null!;
    /// <summary>The screen to show once the loading screen covers the one shown; the latest asked wins.</summary>
    Node? pending;
    bool swapping;
    /// <summary>The first screen is up: a capture counts its frames from there.</summary>
    bool started;
    int frames;

    public override async void _Ready()
    {
        Instance = this;
        RenderingServer.SetDefaultClearColor(Palette.Bg);
        Dev.Parse(OS.GetCmdlineUserArgs());
        // A capture is silent: whoever works next to it hears nothing, whatever the audio driver.
        // Recording the game's sound is the one capture that keeps it, through its own bus effect.
        if ((Dev.Shot is not null || Dev.Snap is not null) && Dev.MusicRecord is null) AudioServer.SetBusMute(0, true);
        if (Dev.MusicDump is string dump)
        {
            Music.Dump(dump);
            GetTree().Quit();
            return;
        }
        if (Dev.SfxDump is string sounds)
        {
            Sfx.Dump(sounds);
            GetTree().Quit();
            return;
        }
        if (Dev.Colors is { } colors) Settings.Current.Colors = colors;
        // A headless service (a dedicated server; the online server and its measure, without even a game):
        // no window needed, nothing to forge, draw or warm up, only the network.
        if (Dev.Service)
        {
            if (Dev.Relay) Network.RelayServer.Run();
            else if (Dev.Probe) Network.Probe.Run();
            else Network.Session.RunServer();
            return;
        }
        // A capture window must not take the keyboard from whoever is typing elsewhere.
        if (Dev.Shot is not null) DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        Lang.Apply(Dev.Lang ?? Settings.Current.Language);
        Display.Apply();
        Bindings.Apply();
        Campaign.GalaxyScreen.MigrateSaves();
        loading = new Ui.Loading();
        AddChild(loading);
        loading.Begin("Forge de la flotte", () => Assets.Progress);
        // The opening plays over it while the game loads; a launch with options (a capture, a
        // start straight into a screen, a network test) goes without, unless it asks for it.
        var intro = Dev.Intro || OS.GetCmdlineUserArgs().Length == 0 ? new Ui.Intro() : null;
        if (intro is not null) AddChild(intro);
        Sfx.Attach(this);
        Music.Attach(this);
        // The menu's theme already under the opening.
        if (intro is not null) Music.Play(MusicTheme.Menu);
        await Assets.Load();
        // The simulation's code compiled out of sight, so that no first decision stalls a frame: once
        // the sprites are forged, as it would take cores from the forge, and well before a first real
        // battle (the opening or the menu come first).
        _ = Task.Run(() =>
        {
            try
            {
                Warmup.Run();
            }
            catch (Exception e)
            {
                GD.PushWarning($"Préchauffage interrompu : {e.Message}");
            }
        });
        if (intro is not null) await intro.Done;
        if (Dev.Dump is string dir) Ui.Logos.LogoImage(Ui.Loading.LogoScale).SavePng($"{dir}/boot.png");
        switch (Dev.Start)
        {
            case "battle" or "raid":
                StartBattle(new BattleOptions
                {
                    Player = Faction.Klaed, Raid = true, Size = Dev.Size, Rules = Dev.Mode, Terrain = Dev.Terrain ?? TerrainDensity.None,
                    // An assault needs its two teams: the player defends.
                    Slots = Dev.Mode == BattleMode.Assault ? [new Slot(Faction.Klaed, Faction.Klaed, 1), new Slot(Faction.Nairan, Faction.Nairan, 2, Difficulty.Normal)]
                        : BattleOptions.Versus(Faction.Klaed, [Faction.Nairan]),
                });
                break;
            case "sandbox" or "gallery": StartBattle(new BattleOptions { Player = Faction.Klaed, Slots = BattleOptions.Versus(Faction.Klaed, [Faction.Nairan]), Mode = GameMode.Sandbox, Size = Dev.Size, Terrain = Dev.Terrain ?? TerrainDensity.None }); break;
            case "campaign": StartCampaign(new MenuChoice { Size = Dev.Size }); break;
            case "tutorial": StartBattle(Ui.Tutorial.Options()); break;
            case "replay" when Dev.Replay is string file && ReplayStore.Load(file, out _) is { } replay: StartBattle(BattleOptions.Watch(replay)); break;
            // With --host, the lobby first: the battle is served (Session.Launch).
            case "stress" when !Dev.Host: StartBattle(new BattleOptions { Player = Faction.Klaed, Slots = BattleOptions.Versus(Faction.Klaed, [Faction.Nairan, Faction.Nautolan]), Size = MapSize.Large, Seed = 7, Terrain = Dev.Terrain ?? TerrainDensity.None }); break;
            default: ShowMenu(); break;
        }
    }

    /// <summary>Leaves the game: the "Quitter" entry of the menu, and the end of a capture.</summary>
    public void QuitGame()
    {
        Music.SaveRecording();
        PlayerProfile.Flush();
        EnsureExit();
        GetTree().Quit();
    }

    /// <summary>
    /// Closing the window quits too (Godot accepts it by itself): the same guarantee. Behind other
    /// windows the game draws fewer frames (Display.Fps); the simulation keeps its fixed steps.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) EnsureExit();
        else if (what == NotificationApplicationFocusOut) Display.Focus(false);
        else if (what == NotificationApplicationFocusIn) Display.Focus(true);
    }

    bool exiting;

    /// <summary>
    /// Once the game is asked to quit, the process ends within a few seconds whatever happens
    /// in the engine's shutdown, so that a terminal never has to interrupt it. Nothing is lost:
    /// the settings and the campaign are saved as they change. A game over the network is left
    /// first: its players, or its host, hear of it at once rather than when the connection times out.
    /// </summary>
    void EnsureExit()
    {
        if (exiting) return;
        exiting = true;
        Network.Session.Current?.Leave();
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(3000);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }) { IsBackground = true }.Start();
    }

    /// <summary>--snap: captures asked for, at the frame they are due, and how many were taken.</summary>
    readonly List<(ulong Frame, string Name)> snaps = [];
    int snapped;

    /// <summary>
    /// --snap: a capture of the screen `after` frames from now (a dialog has faded in), into the
    /// --snap folder, numbered in turn: "03-invite.png". Nothing without --snap.
    /// </summary>
    public void Snap(string name, int after = 20)
    {
        if (Dev.Snap is not null) snaps.Add((Engine.GetProcessFrames() + (ulong)after, name));
    }

    void TakeSnaps()
    {
        for (var i = snaps.Count - 1; i >= 0; i--)
        {
            if (Engine.GetProcessFrames() < snaps[i].Frame) continue;
            var path = $"{Dev.Snap}/{++snapped:00}-{snaps[i].Name}.png";
            var err = GetViewport().GetTexture()?.GetImage()?.SavePng(path) ?? Error.Unavailable;
            GD.Print($"Capture {path} : {err}");
            snaps.RemoveAt(i);
        }
    }

    public override void _Process(double delta)
    {
        PlayerProfile.Tick(screen, delta);
        if (snaps.Count > 0) TakeSnaps();
        if (Dev.Shot is null || !started || ++frames < Dev.Frames) return;
        // Without a window (--headless) there is nothing to capture: just quit.
        var image = GetViewport().GetTexture()?.GetImage();
        var err = image?.SavePng(Dev.Shot) ?? Error.Unavailable;
        GD.Print($"Capture {Dev.Shot} : {err}, {Engine.GetFramesPerSecond()} images/s");
        QuitGame();
    }

    /// <summary>
    /// Replaces the screen shown, behind the loading screen: it fades in, the next screen is
    /// built under it, and it fades out.
    /// </summary>
    public void Show(Node next)
    {
        // A screen asked for and replaced before it was shown was never in the tree.
        if (pending is not null && pending != next && pending != galaxy && !pending.IsInsideTree()) pending.Free();
        pending = next;
        if (!swapping) Swap();
    }

    async void Swap()
    {
        swapping = true;
        // The shown screen stops under the loading screen: nothing it would do now could be seen.
        if (screen is not null) screen.ProcessMode = ProcessModeEnum.Disabled;
        while (pending is { } next)
        {
            await loading.Cover(next switch
            {
                BattleScreen => "Préparation de la bataille",
                Campaign.GalaxyScreen => "Chargement de la galaxie",
                _ => "",
            });
            // Asked again meanwhile: the latest wins.
            next = pending;
            pending = null;
            if (next is not null) Place(next);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        swapping = false;
        started = true;
        loading.Reveal();
    }

    /// <summary>
    /// Puts the next screen in place of the shown one. The campaign's galaxy sleeps (hidden,
    /// not playing) while one of its battles is shown, and wakes after it; any other screen
    /// ends the campaign.
    /// </summary>
    void Place(Node next)
    {
        var campaignBattle = next is BattleScreen { Opts.Campaign: not null };
        if (screen is not null)
        {
            if (screen == galaxy && campaignBattle) galaxy.Sleep();
            else
            {
                // Freed at the end of the frame: it must not play that frame any more.
                screen.ProcessMode = ProcessModeEnum.Disabled;
                screen.QueueFree();
            }
        }
        if (galaxy is not null && next != galaxy && !campaignBattle)
        {
            if (galaxy != screen) galaxy.QueueFree();
            galaxy = null;
        }
        screen = next;
        if (galaxy is not null && next == galaxy && galaxy.IsInsideTree()) galaxy.Wake();
        else AddChild(next);
        Music.Play(next switch
        {
            Ui.Menu => MusicTheme.Menu,
            Campaign.GalaxyScreen => MusicTheme.Galaxy,
            BattleScreen => MusicTheme.Battle,
            _ => MusicTheme.None,
        });
    }

    /// <summary>The start screen: the main menu, which calls Start with what the player chose.</summary>
    public void ShowMenu() => Show(new Ui.Menu());

    /// <summary>What the menu's lobby starts: the campaign, or a battle under its rules; over the network, the battle `net` holds.</summary>
    public void Start(Lobby lobby, Network.NetBattle? net = null)
    {
        var me = net?.Side ?? lobby.SideOfPeer() ?? Lobby.SideOf(0);
        if (lobby.Mode == LobbyMode.Campaign)
        {
            StartCampaign(new MenuChoice { Seats = lobby.Slots(), Me = me, Map = lobby.Map, Seed = lobby.Seed, Size = lobby.Size });
            return;
        }
        var o = lobby.Skirmish();
        StartBattle(new BattleOptions
        {
            Player = me, Slots = o.Slots, Mode = lobby.Mode == LobbyMode.Sandbox ? GameMode.Sandbox : GameMode.Skirmish,
            Seed = o.Seed, Map = o.Map, Size = o.Size, Raid = o.Raid, Terrain = o.Terrain, Fog = o.Fog, Rules = o.Mode, ZoneTarget = o.ZoneTarget, AssaultMinutes = o.AssaultMinutes,
            Net = net,
        });
    }

    public void StartBattle(BattleOptions opts) => Show(BattleScreen.Create(opts));

    /// <summary>The galaxy of the campaign under way: shown, or asleep during one of its battles.</summary>
    Campaign.GalaxyScreen? galaxy;

    /// <summary>A new or resumed galaxy campaign.</summary>
    public void StartCampaign(MenuChoice choice)
    {
        var next = Campaign.GalaxyScreen.Create(choice);
        Show(next);
        galaxy = next;
    }

    /// <summary>A campaign battle is decided: back to the galaxy with its result.</summary>
    public void FinishCampaignBattle(int battleId, BattleResult result)
    {
        if (galaxy is null)
        {
            ShowMenu();
            return;
        }
        galaxy.FinishBattle(battleId, result);
        Show(galaxy);
    }

    /// <summary>The player, attacking, falls back from a campaign battle: back to the galaxy, where the survivors head home.</summary>
    public void RetreatCampaignBattle(int battleId)
    {
        if (galaxy is null)
        {
            ShowMenu();
            return;
        }
        galaxy.Retreat(battleId);
        Show(galaxy);
    }

    /// <summary>Back to the galaxy from one of its battles, nothing decided here: over the network, the host decides.</summary>
    public void ShowGalaxy()
    {
        if (galaxy is null) ShowMenu();
        else Show(galaxy);
    }

    /// <summary>The player leaves a campaign battle, which plays on by itself.</summary>
    public void DelegateCampaignBattle(int battleId)
    {
        if (galaxy is null)
        {
            ShowMenu();
            return;
        }
        galaxy.DelegateBattle(battleId);
        Show(galaxy);
    }
}
