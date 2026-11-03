// The keys the player may change: an InputMap action each, made at the start from the defaults
// below and the player's own choices (Settings.Keys). The battle and the galaxy ask the actions,
// never the keys; the controls lists name the keys the actions really have (Label, Movement).
using Godot;

namespace SandboxShip.Core;

public static class Bindings
{
    /// <summary>Where a command is used: two commands used in the same place may not share a key.</summary>
    [Flags]
    public enum Where { Battle = 1, Galaxy = 2, Both = Battle | Galaxy }

    /// <summary>
    /// A key and its modifiers: the key as printed on it (Key), or by its place on the keyboard
    /// (Physical), as the view's keys are: ZQSD on AZERTY, WASD on QWERTY.
    /// </summary>
    public readonly record struct Combo(Key Key, bool Physical = false, bool Ctrl = false, bool Shift = false, bool Alt = false)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Modified => Ctrl || Shift || Alt;
    }

    /// <summary>A command with a key of the player's choice; the alternates (arrows, keypad) stay beside it whatever it is.</summary>
    public sealed record Binding(string Id, string Name, Where Where, Combo Default, params Combo[] Alternates);

    public const string PanUp = "pan_up", PanDown = "pan_down", PanLeft = "pan_left", PanRight = "pan_right",
        ZoomIn = "zoom_in", ZoomOut = "zoom_out", Pause = "pause", Stop = "stop",
        SelectAll = "select_all", AttackMove = "attack_move", Patrol = "patrol", Guard = "guard", Stance = "stance",
        Ability = "ability", Pilot = "pilot", CenterSelection = "center_selection",
        CenterStation = "center_station", LastAlert = "last_alert", Idle = "idle", Mute = "mute",
        Overview = "overview", Fleets = "fleets", Empire = "empire", Log = "log", Diplomacy = "diplomacy";

    static Combo Phys(Key k) => new(k, Physical: true);

    /// <summary>Every command with a key to change, in the order the controls screen lists them.</summary>
    public static readonly IReadOnlyList<Binding> All =
    [
        new(PanUp, "Vue vers le haut ; en pilotant, avancer", Where.Both, Phys(Key.W), new Combo(Key.Up)),
        new(PanLeft, "Vue vers la gauche ; en pilotant, tourner", Where.Both, Phys(Key.A), new Combo(Key.Left)),
        new(PanDown, "Vue vers le bas ; en pilotant, reculer", Where.Both, Phys(Key.S), new Combo(Key.Down)),
        new(PanRight, "Vue vers la droite ; en pilotant, tourner", Where.Both, Phys(Key.D), new Combo(Key.Right)),
        new(ZoomIn, "Zoom avant", Where.Both, new Combo(Key.Equal), new Combo(Key.Plus), new Combo(Key.KpAdd)),
        new(ZoomOut, "Zoom arrière", Where.Both, new Combo(Key.Minus), new Combo(Key.KpSubtract)),
        new(Pause, "Pause", Where.Both, new Combo(Key.Space)),
        new(Stop, "Stop ; sur la carte, arrêter la flotte en route", Where.Both, new Combo(Key.X)),
        new(SelectAll, "Tous tes vaisseaux", Where.Battle, new Combo(Key.A, Ctrl: true)),
        new(AttackMove, "Attaque-mouvement, puis clic", Where.Battle, new Combo(Key.F)),
        new(Patrol, "Patrouille, puis clic (Maj + clics : plusieurs points)", Where.Battle, new Combo(Key.R)),
        new(Guard, "Garder ou escorter, puis clic sur un allié", Where.Battle, new Combo(Key.G)),
        new(Stance, "Posture suivante : agressive, défensive, tenir, passive", Where.Battle, new Combo(Key.V)),
        new(Ability, "Capacité des vaisseaux sélectionnés", Where.Battle, new Combo(Key.E)),
        new(Pilot, "Piloter le vaisseau sélectionné", Where.Battle, new Combo(Key.P)),
        new(CenterSelection, "Centrer la vue sur la sélection", Where.Battle, new Combo(Key.C)),
        new(CenterStation, "Centrer la vue sur la station", Where.Battle, new Combo(Key.H)),
        new(LastAlert, "Aller à la dernière alerte", Where.Battle, new Combo(Key.Backspace)),
        new(Idle, "Vaisseau inactif suivant (Maj : tous)", Where.Battle, new Combo(Key.I)),
        new(Mute, "Couper ou rétablir le son", Where.Battle, new Combo(Key.M)),
        new(Overview, "Revoir toute la galaxie", Where.Galaxy, new Combo(Key.H), new Combo(Key.Home)),
        new(Fleets, "Volet des flottes", Where.Galaxy, new Combo(Key.F)),
        new(Empire, "Vue d'ensemble de l'empire", Where.Galaxy, new Combo(Key.E)),
        new(Log, "Journal", Where.Galaxy, new Combo(Key.J)),
        new(Diplomacy, "Diplomatie", Where.Galaxy, new Combo(Key.T)),
    ];

    static readonly Dictionary<string, Binding> byId = All.ToDictionary(b => b.Id);

    public static Binding Find(string id) => byId[id];

    /// <summary>The key a command has now: the player's, or its default.</summary>
    public static Combo Of(string id) => Settings.Current.Keys.TryGetValue(id, out var c) ? c : byId[id].Default;

    /// <summary>Makes every action as the settings have it; at the start, and again after a change.</summary>
    public static void Apply()
    {
        foreach (var b in All)
        {
            if (!InputMap.HasAction(b.Id)) InputMap.AddAction(b.Id);
            InputMap.ActionEraseEvents(b.Id);
            InputMap.ActionAddEvent(b.Id, Event(Of(b.Id)));
            foreach (var alt in b.Alternates) InputMap.ActionAddEvent(b.Id, Event(alt));
        }
    }

    static InputEventKey Event(Combo c) => new()
    {
        Keycode = c.Physical ? Key.None : c.Key, PhysicalKeycode = c.Physical ? c.Key : Key.None,
        CtrlPressed = c.Ctrl, ShiftPressed = c.Shift, AltPressed = c.Alt,
    };

    /// <summary>Gives a command a new key, kept in the settings unless it is the default.</summary>
    public static void Set(string id, Combo c)
    {
        if (c == byId[id].Default) Settings.Current.Keys.Remove(id);
        else Settings.Current.Keys[id] = c;
        Settings.Current.Save();
        Apply();
    }

    /// <summary>Every command back to its default key.</summary>
    public static void Reset()
    {
        Settings.Current.Keys.Clear();
        Settings.Current.Save();
        Apply();
    }

    /// <summary>The other commands used in the same place that `id`'s key also triggers.</summary>
    public static IEnumerable<Binding> Conflicts(string id)
    {
        var mine = byId[id];
        var key = Of(id);
        return All.Where(o => o.Id != id && (o.Where & mine.Where) != 0 && (Of(o.Id) == key || o.Alternates.Contains(key)));
    }

    /// <summary>
    /// Whether `ev` presses `id`'s key: with exactly its modifiers when it has some (Ctrl + A is not
    /// A), with any otherwise (Maj + I is still I). Held keys repeat, as before.
    /// </summary>
    public static bool Pressed(InputEvent ev, string id) => ev.IsActionPressed(id, true, Of(id).Modified);

    /// <summary>Whether `id`'s key, or one of its alternates, is held down.</summary>
    public static bool Held(string id) => Input.IsActionPressed(id) && !Typing;

    /// <summary>A line of text (the chat) has the keyboard: what is typed there moves no view and flies no ship.</summary>
    static bool Typing => ((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner() is LineEdit;

    static readonly Dictionary<Key, string> Names = new()
    {
        [Key.Space] = "Espace", [Key.Backspace] = "Retour arrière", [Key.Escape] = "Échap", [Key.Enter] = "Entrée",
        [Key.KpEnter] = "Entrée du pavé", [Key.Tab] = "Tab", [Key.Home] = "Début", [Key.End] = "Fin", [Key.Delete] = "Suppr",
        [Key.Insert] = "Inser", [Key.Pageup] = "Page préc.", [Key.Pagedown] = "Page suiv.", [Key.Up] = "Haut", [Key.Down] = "Bas",
        [Key.Left] = "Gauche", [Key.Right] = "Droite", [Key.Equal] = "+", [Key.Plus] = "+", [Key.Minus] = "-",
        [Key.KpAdd] = "+ du pavé", [Key.KpSubtract] = "- du pavé", [Key.Capslock] = "Verr. maj.",
    };

    /// <summary>A key as the player's keyboard prints it: a physical key under its letter on this layout.</summary>
    public static string KeyName(Combo c)
    {
        var key = c.Physical ? DisplayServer.KeyboardGetKeycodeFromPhysical(c.Key) : c.Key;
        var name = Names.TryGetValue(key, out var n) ? n : OS.GetKeycodeString(key);
        return (c.Ctrl ? "Ctrl + " : "") + (c.Alt ? "Alt + " : "") + (c.Shift ? "Maj + " : "") + name;
    }

    /// <summary>The key of a command, as the controls lists, the help and the tutorial show it: "E", "Ctrl + A", "Espace".</summary>
    public static string Label(string id) => KeyName(Of(id));

    /// <summary>The four view keys together: "ZQSD" when they are single letters, else listed up, left, down, right.</summary>
    public static string Movement()
    {
        string[] keys = [Label(PanUp), Label(PanLeft), Label(PanDown), Label(PanRight)];
        return keys.All(k => k.Length == 1) ? string.Concat(keys) : string.Join(", ", keys);
    }
}
