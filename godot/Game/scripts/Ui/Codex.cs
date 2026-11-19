// The codex: every ship class, hero and race, who beats whom (Sim/Counters.cs). One screen, on
// the main menu (Menu.cs, a screen of its own) and in a dialog of the battle's pause menu and of
// the galaxy's help (Panel); time stands still behind it there, as behind the other menus. Its
// texts are those of the panels where they exist: weapons as the production tooltip names them,
// abilities and heroes as the selection panel tells them, traits as the top bar's tooltip.
using System.Globalization;
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Codex : VBoxContainer
{
    public enum Kind { Ships, Heroes, Races }

    /// <summary>What each class is for, in a line: the one text the data does not hold.</summary>
    static readonly Dictionary<ShipClass, string> Roles = new()
    {
        [ShipClass.Scout] = "Rapide et fragile : il part devant la flotte et harcèle les appareils.",
        [ShipClass.Fighter] = "Appareil d'interception, nombreux et rapide, contre les petits vaisseaux.",
        [ShipClass.Bomber] = "Torpilles contre les grosses coques et les bâtiments ; sans défense contre la flak.",
        [ShipClass.Support] = "Répare les alliés autour de lui ; sa flak tient les appareils à distance.",
        [ShipClass.Torpedo] = "Torpilles lourdes à tête chercheuse : la réponse aux grosses coques.",
        [ShipClass.Frigate] = "Le vaisseau de ligne de base : rayon continu, flak et générateur de bouclier.",
        [ShipClass.Battlecruiser] = "Vaisseau de ligne lourd : laser, canon à rafales et flak.",
        [ShipClass.Bulwark] = "Un mur : solide comme un cuirassé pour moins cher, il attire le feu ennemi.",
        [ShipClass.Carrier] = "Presque sans canons, mais deux grands hangars qui lancent sans cesse.",
        [ShipClass.Dreadnought] = "Le poids lourd de la ligne : rayon lourd, lasers, ions, flak et hangar.",
        [ShipClass.Titan] = "Vaisseau capital : deux rayons lourds sur la quille, missiles et hangar.",
        [ShipClass.Leviathan] = "Au-dessus du Titan : batterie spinale, bordées sur les deux flancs, deux hangars.",
        [ShipClass.Colossus] = "Le vaisseau ultime, au prix d'une flotte entière : ses quatre générateurs de bouclier sont la clé.",
        [ShipClass.Artillery] = "Obus de siège à très longue portée contre les défenses, perdu de près.",
        [ShipClass.Corvette] = "Rapide, par deux : son écran de flak abat appareils, torpilles et obus.",
        [ShipClass.Jammer] = "À peine armé : son brouillage raccourcit la portée ennemie et coupe la recharge des boucliers.",
        [ShipClass.Stealth] = "Invisible de loin tant qu'il ne tire pas : il chasse les mines et les avant-postes.",
    };

    static string SizeName(SizeClass s) => s switch
    {
        SizeClass.Small => "Petit vaisseau",
        SizeClass.Medium => "Vaisseau moyen",
        _ => "Grand vaisseau",
    };

    /// <summary>The list's width, and the picture's size in the detail.</summary>
    const float ListW = 236;
    static readonly Vector2 PictureSize = new(232, 132);

    Faction race;
    Kind kind;
    ShipClass cls = ShipClass.Frigate;
    HeroRole hero = HeroRole.Admiral;
    /// <summary>The race the Races page shows: one of the playable three, or the Errants.</summary>
    Faction shownRace;
    readonly bool focusable;
    readonly HBoxContainer kinds = Row(4), races = Row(4);
    readonly VBoxContainer list = Column(4), detail = Column(14);
    readonly ScrollContainer listScroll, detailScroll;

    /// <summary>
    /// The codex in `race`'s colors and prices. `focusable`: reachable with the keyboard, as on the
    /// menu (in battle, Space and Enter stay the game's). `open`: what it shows first, as --codex
    /// names it (Open).
    /// </summary>
    public Codex(Faction race, bool focusable = false, string? open = null)
    {
        this.race = shownRace = Factions.All.Contains(race) ? race : Faction.Klaed;
        this.focusable = focusable;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);
        var top = new HFlowContainer();
        top.AddThemeConstantOverride("h_separation", 24);
        top.AddThemeConstantOverride("v_separation", 8);
        top.AddChild(kinds);
        top.AddChild(races);
        AddChild(top);
        var panes = Row(16);
        panes.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(panes);
        // Room for the scroll bars, which would otherwise cover the end of the lines.
        var listPad = Ignore(new MarginContainer());
        listPad.AddThemeConstantOverride("margin_right", 12);
        listPad.AddChild(list);
        listScroll = Scroll(listPad);
        listScroll.CustomMinimumSize = new Vector2(ListW, 0);
        listScroll.SizeFlagsHorizontal = SizeFlags.Fill;
        panes.AddChild(listScroll);
        var pad = Ignore(new MarginContainer());
        pad.AddThemeConstantOverride("margin_right", 14);
        pad.AddChild(detail);
        detailScroll = Scroll(pad);
        detailScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panes.AddChild(detailScroll);
        if (open is not null) Open(open);
        Rebuild();
    }

    /// <summary>
    /// What --codex asks to show first: a ship class ("frigate"), a hero by role ("hero-admiral"),
    /// a race ("race-nairan"), each perhaps after a race to show it in ("nairan:frigate").
    /// </summary>
    void Open(string what)
    {
        if (what.Split(':', 2) is [var r, var rest] && Enum.TryParse<Faction>(r, true, out var f) && Factions.All.Contains(f))
        {
            race = shownRace = f;
            what = rest;
        }
        if (what.StartsWith("hero-", StringComparison.Ordinal) && Enum.TryParse<HeroRole>(what[5..], true, out var role)) (kind, hero) = (Kind.Heroes, role);
        else if (what.StartsWith("race-", StringComparison.Ordinal) && Enum.TryParse<Faction>(what[5..], true, out var shown))
        {
            kind = Kind.Races;
            ShowRace(shown);
        }
        else if (Enum.TryParse<ShipClass>(what, true, out var c)) (kind, cls) = (Kind.Ships, c);
    }

    /// <summary>
    /// The codex as a dialog, for the battle's pause menu and the galaxy's help: as large as the
    /// screen allows, its button to close it under it. `onClose` runs when it is closed.
    /// </summary>
    public static PanelContainer Panel(Faction race, Action onClose, string? open = null)
    {
        var p = HudKit.Panel(24, 20);
        var box = Column(12);
        p.AddChild(box);
        var head = Row(16);
        head.AddChild(Text("Codex", size: 22, bold: true));
        var note = Text("Qui bat qui : chaque classe, ses armes et ses contres.", Palette.Muted, 14);
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(note);
        box.AddChild(head);
        var screen = ((SceneTree)Engine.GetMainLoop()).Root.GetVisibleRect().Size;
        var codex = new Codex(race, open: open)
        {
            CustomMinimumSize = new Vector2(Math.Min(1040, screen.X - 80), Math.Max(320, Math.Min(620, screen.Y - 190))),
        };
        box.AddChild(codex);
        var close = Primary(Button("Fermer", onClose));
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        box.AddChild(close);
        return p;
    }

    /// <summary>A button whose action runs once the click is over, as it rebuilds the codex under it.</summary>
    Button Btn(string text, Action act, string tip = "", int height = 36)
    {
        var b = Button(text, null, tip, height);
        b.Pressed += () => Callable.From(act).CallDeferred();
        if (focusable) b.FocusMode = FocusModeEnum.All;
        return b;
    }

    void Rebuild()
    {
        foreach (var box in new Control[] { kinds, races, list, detail })
        {
            foreach (var child in box.GetChildren())
            {
                box.RemoveChild(child);
                child.QueueFree();
            }
        }
        foreach (var (k, label) in new[] { (Kind.Ships, "Vaisseaux"), (Kind.Heroes, "Héros"), (Kind.Races, "Races") })
        {
            var b = Btn(label, () => Show(k));
            Outline(b, k == kind ? Palette.Accent : null);
            kinds.AddChild(b);
        }
        // The race whose sprites, prices and heroes show: the player's at first.
        foreach (var f in Factions.All)
        {
            var b = Btn(Factions.Defs[f].Name, () => SetRace(f), $"{Factions.Defs[f].Trait.Name} : {Factions.Defs[f].Trait.Description}");
            b.Icon = Logos.Emblem(f, 1);
            Outline(b, f == race ? Palette.Accent : null);
            races.AddChild(b);
        }
        switch (kind)
        {
            case Kind.Ships:
                foreach (var c in Ships.All) list.AddChild(Entry(Ships.Defs[c].Name, ShortPrice(c), Assets.ShipIcon(race, c), c == cls, () => ShowShip(c)));
                ShipDetail(cls);
                break;
            case Kind.Heroes:
                foreach (var h in Heroes.Of(race))
                {
                    var role = h.Id.Role;
                    list.AddChild(Entry(h.Name, h.Title, Assets.ShipIcon(race, h.Base), role == hero, () => ShowHero(role)));
                }
                HeroDetail(Heroes.Defs[new HeroId(race, hero)]);
                break;
            case Kind.Races:
                foreach (var f in Factions.All.Append(Faction.Errant))
                    list.AddChild(Entry(Factions.Defs[f].Name, Factions.Defs[f].Trait.Name, Logos.Emblem(f, 2), f == shownRace, () => ShowRace(f)));
                RaceDetail(shownRace);
                break;
        }
        detailScroll.ScrollVertical = 0;
    }

    void Show(Kind k)
    {
        kind = k;
        Rebuild();
    }

    void SetRace(Faction f)
    {
        race = shownRace = f;
        Rebuild();
    }

    void ShowShip(ShipClass c) => (kind, cls) = (Kind.Ships, c);

    void ShowHero(HeroRole role) => (kind, hero) = (Kind.Heroes, role);

    void ShowRace(Faction f)
    {
        shownRace = f;
        if (Factions.All.Contains(f)) race = f;
    }

    /// <summary>A line of the list: its picture, its name and a smaller line; a click shows it, rebuilt once the click is over.</summary>
    LabelButton Entry(string name, string small, Texture2D? icon, bool on, Action pick)
    {
        var b = new LabelButton(true, 8, 3) { MinSize = new Vector2(0, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (focusable) b.FocusMode = FocusModeEnum.All;
        b.Add(name, Palette.Text, 14, wrap: true);
        b.Add(small, Palette.Muted, 12, wrap: true);
        b.SetIcon(icon);
        b.Marked = on;
        b.Paint();
        Outline(b, on ? Palette.Accent : null, false);
        b.Pressed += () => Callable.From(() =>
        {
            pick();
            Rebuild();
        }).CallDeferred();
        return b;
    }

    string ShortPrice(ShipClass c) => $"{N(Ships.Cost(c, race))} cr, {N(Ships.Defs[c].BuildTime)} s";

    /// <summary>A multiplier as the language writes it: "×2,5" in French.</summary>
    static string Times(double m) => "×" + m.ToString("0.##", Lang.Current == Lang.French ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.InvariantCulture);

    static Label Heading(string text) => Text(text, Palette.Text, 15, bold: true);

    /// <summary>A paragraph wrapped to the width the detail gives it.</summary>
    static Label Para(string text, Color? color = null)
    {
        var l = Text(text, color, wrap: true);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return l;
    }

    /// <summary>The picture, then beside it the name, a line and smaller lines under them.</summary>
    void Header(Control picture, string name, string line, params string[] smaller)
    {
        var row = Row(16);
        row.AddChild(picture);
        var col = Column(4);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.AddChild(Text(name, Palette.Accent, 20, bold: true));
        col.AddChild(Para(line));
        foreach (var s in smaller) col.AddChild(Para(s, Palette.Muted));
        row.AddChild(col);
        detail.AddChild(row);
    }

    /// <summary>Figures side by side, each its name above its value.</summary>
    void Figures(params (string Name, string Value)[] figures)
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 28);
        flow.AddThemeConstantOverride("v_separation", 8);
        foreach (var (name, value) in figures)
        {
            var col = Column(0);
            col.AddChild(Text(name, Palette.Muted, 13));
            col.AddChild(Text(value, Palette.Text, 17, bold: true));
            flow.AddChild(col);
        }
        detail.AddChild(flow);
    }

    void Section(string title, params Control[] items)
    {
        var col = Column(6);
        col.AddChild(Heading(title));
        foreach (var i in items) col.AddChild(i);
        detail.AddChild(col);
    }

    /// <summary>An ability, its name over what it does.</summary>
    static Control AbilityLine(AbilityId id)
    {
        var col = Column(0);
        col.AddChild(Text(Abilities.Defs[id].Name, Palette.Accent, 15));
        col.AddChild(Para(Abilities.Defs[id].Description, Palette.Muted));
        return col;
    }

    /// <summary>The matchups, a button each that shows that class; the buildings, a plain chip.</summary>
    Control Matchups(IEnumerable<ShipClass?> foes, string none)
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var foe in foes)
        {
            if (foe is not ShipClass c)
            {
                var chip = Ignore(HudKit.Panel(8, 4));
                var row = Row(6);
                row.AddChild(Ignore(new TextureRect { Texture = Assets.StationIcon(race, 1), StretchMode = TextureRect.StretchModeEnum.KeepCentered }));
                row.AddChild(Text("Bâtiments", size: 14));
                chip.AddChild(row);
                flow.AddChild(chip);
                continue;
            }
            var b = Btn(Ships.Defs[c].Name, () =>
            {
                ShowShip(c);
                Rebuild();
            }, height: 40);
            b.Icon = Assets.ShipIcon(race, c);
            flow.AddChild(b);
        }
        if (flow.GetChildCount() == 0) flow.AddChild(Text(none, Palette.Muted, 14));
        return flow;
    }

    /// <summary>Strong against, weak against: the counters of a class, worked out from the data.</summary>
    void Counters(ShipClass c)
    {
        Section("Forte contre", Matchups(Sim.Counters.StrongAgainst(c), "Aucune cible de prédilection"));
        Section("Faible contre", Matchups(Sim.Counters.WeakAgainst(c).Select(f => (ShipClass?)f), "Aucune faiblesse nette"));
    }

    /// <summary>What the weapons say that their names do not: point defense, ions, a reach past the defenses.</summary>
    static List<string> Notes(ShipClass c)
    {
        var notes = new List<string>();
        var weapons = Sim.Counters.WeaponsOf(c).ToList();
        if (weapons.Any(w => w.PointDefense)) notes.Add("Défense ponctuelle : sa flak abat les torpilles, les missiles et les obus en vol.");
        if (weapons.FirstOrDefault(w => w.ShieldMult != 1 || w.HullMult != 1) is { } ion)
            notes.Add($"{Times(ion.ShieldMult)} contre les boucliers, {Times(ion.HullMult)} contre la coque ({Names.WeaponName(ion)}).");
        // A reach past the defenses, all of them or all but one: the siege gun's, the capital ships'.
        var range = Sim.Counters.RangeOf(c);
        var short_ = Sim.Counters.Defenses().Where(d => d.Range >= range).Select(d => d.Name).ToList();
        if (short_.Count == 0) notes.Add($"Portée {N(range)} : plus loin que toutes les défenses.");
        else if (short_.Count == 1) notes.Add($"Portée {N(range)} : plus loin que toutes les défenses sauf : {short_[0]}.");
        return notes;
    }

    void ShipDetail(ShipClass c)
    {
        var def = Ships.Defs[c];
        var trait = Factions.Trait(race);
        var lines = new List<string> { SizeName(def.Size) };
        if (def.Limit is not null) lines.Add(def.LimitReason);
        lines.Add($"{Ships.OrderName(c)} : {N(Ships.Cost(c, race))} cr, {N(def.BuildTime)} s, station niveau {def.MinStationLevel}");
        Header(new ShipPicture(race, c), def.Name, Roles[c], [.. lines]);
        Figures(("Coque", N(Js.Round(def.Hull * trait.Hull))), ("Bouclier", N(Js.Round(def.Shield * trait.Shield))),
            ("Vitesse", N(Js.Round(def.Speed * trait.Speed))), ("Portée", N(Sim.Counters.RangeOf(c))));
        Section("Armes", Para(Names.Armament(c)));
        IEnumerable<AbilityId> abilities = def.Ability is AbilityId a ? [a, .. def.Extra ?? []] : [];
        if (abilities.Any()) Section(abilities.Count() > 1 ? "Capacités" : "Capacité", [.. abilities.Select(AbilityLine)]);
        Counters(c);
        foreach (var note in Notes(c)) detail.AddChild(Para(note, Palette.Muted));
    }

    void HeroDetail(HeroDef h)
    {
        var trait = Factions.Trait(race);
        var ship = Ships.Defs[h.Base];
        Header(new ShipPicture(race, h.Base), h.Name, h.Description, $"{h.Title}, vaisseau : {ship.Name}",
            $"{N(h.Cost)} cr, {N(h.BuildTime)} s, station niveau {h.MinStationLevel}. Reconstruit {N(h.Respawn)} s après sa perte");
        Figures(("Coque", N(Js.Round(ship.Hull * h.Hull * trait.Hull))), ("Bouclier", N(Js.Round(ship.Shield * h.Shield * trait.Shield))),
            ("Vitesse", N(Js.Round(ship.Speed * h.Speed * trait.Speed))), ("Portée", N(Sim.Counters.RangeOf(h.Base))), ("Dégâts", Times(h.Damage)));
        Section("Armes", Para(Names.Armament(h.Base)));
        Section("Pouvoir", AbilityLine(h.Ability));
        if (h.Id.Role == HeroRole.Admiral)
            detail.AddChild(Para($"Commandement : à {N(Abilities.CommandRadius)} ou moins de lui, les alliés font plus de dégâts et vont plus vite.", Palette.Muted));
        Counters(h.Base);
    }

    void RaceDetail(Faction f)
    {
        var def = Factions.Defs[f];
        var emblem = Ignore(new TextureRect
        {
            Texture = Logos.Emblem(f, 6), StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(PictureSize.Y, PictureSize.Y),
        });
        Header(emblem, def.Name, $"{def.Trait.Name} : {def.Trait.Description}");
        var heroes = Heroes.Of(f).ToList();
        if (heroes.Count == 0) return;
        var col = Column(8);
        foreach (var h in heroes)
        {
            var role = h.Id.Role;
            var b = new LabelButton(true, 8, 3) { MinSize = new Vector2(0, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            if (focusable) b.FocusMode = FocusModeEnum.All;
            b.Add($"{h.Name}, {h.Title.ToLowerInvariant()}", Palette.Text, 14, wrap: true);
            b.Add(h.Description, Palette.Muted, 12, wrap: true);
            b.SetIcon(Assets.ShipIcon(f, h.Base));
            b.Paint();
            b.Pressed += () => Callable.From(() =>
            {
                race = f;
                ShowHero(role);
                Rebuild();
            }).CallDeferred();
            col.AddChild(b);
        }
        Section("Héros", col);
    }

    /// <summary>
    /// A ship as the battle draws it (BattleScreen.CreateView, --start=gallery): its hull's layers
    /// in its race's colors, and over them each of its parts on its slot, bow to the right.
    /// </summary>
    sealed partial class ShipPicture : Control
    {
        /// <summary>Screen pixels a world unit takes at most: a scout stays a scout, not a smear.</summary>
        const double MostScale = 3.5;
        const double Facing = Math.PI / 2;
        readonly Faction race;
        readonly ShipClass cls;

        public ShipPicture(Faction race, ShipClass cls)
        {
            this.race = race;
            this.cls = cls;
            CustomMinimumSize = PictureSize;
            MouseFilter = MouseFilterEnum.Ignore;
            ClipContents = true;
            Resized += () => TextureFilter = PixelScale() < 1 ? TextureFilterEnum.Linear : TextureFilterEnum.Nearest;
        }

        Look Look => Assets.ShipLook(race, cls);

        /// <summary>Screen pixels per world unit: the hull's length fits the width, its girth the height.</summary>
        double PixelScale()
        {
            var def = Ships.Defs[cls];
            var length = 2 * def.Radius * Math.Max(1, Look.Layers.Max(l => l.Size));
            var girth = def.Beam > 0 ? 3.2 * def.Beam : length;
            return Math.Min(MostScale, Math.Min((Size.X - 20) / length, (Size.Y - 16) / girth));
        }

        public override void _Draw()
        {
            DrawStyleBox(UiTheme.Box(Palette.Bg with { A = 0.55f }, Palette.Line), new Rect2(Vector2.Zero, Size));
            var def = Ships.Defs[cls];
            var look = Look;
            var k = PixelScale();
            var mid = Size / 2;
            var tint = look.Tint ? Palette.Of(race) : Colors.White;
            void Sprite(string key, double radius, double size, Vector2 at)
            {
                var tex = Assets.Frame(key);
                var s = (float)(radius * 2 * size / Assets.OpaqueSize(key) * k);
                DrawSetTransform(at, (float)Facing, new Vector2(s, s));
                DrawTexture(tex, -tex.GetSize() / 2, tint);
            }
            // Layers are placed in the sprite's frame, bow up: a quarter turn brings the bow to the right.
            foreach (var l in look.Layers) Sprite(l.Key, def.Radius, l.Size, mid + new Vector2((float)(-l.Y * def.Radius * k), (float)(l.X * def.Radius * k)));
            tint = Colors.White;
            foreach (var slot in def.Hardpoints ?? [])
            {
                var weapon = (slot.Weapons ?? Hardpoints.Defs[slot.Role].Weapons).FirstOrDefault();
                if (Assets.PartLook(race, slot.Role, weapon) is not { } part) continue;
                var layer = part.Layers[0];
                Sprite(layer.Key, slot.Radius ?? Hardpoints.Defs[slot.Role].Radius, layer.Size, mid + new Vector2((float)(slot.X * k), (float)(slot.Y * k)));
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
