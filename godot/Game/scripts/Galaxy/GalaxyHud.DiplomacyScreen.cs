// The diplomacy screen of the campaign (key T): a dialogue with one faction at a time. The
// factions on the left; on the right, the one chosen: how it feels about the player and why, and
// its trust; five intentions (make peace, trade, ally, demand, war), each with how the AI leans
// towards it, opening on the forms it takes; the accords in force and how to end them; and the
// thread of what passed between them: proposals, answers, counter-offers, and the offers and
// demands it makes, answered there. The table of relations between all factions and the
// chronicle are on a second tab. Alone, time stops while it is open.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Campaign;

public partial class GalaxyHud
{
    enum DiploTab { Talk, Relations }

    /// <summary>The intentions that open on their forms; war opens nothing, it ends the peace there is.</summary>
    internal enum Intent { None, Peace, Trade, Ally, Demand }

    /// <summary>Shows the second tab, as a click on it does (the captures of --deals=relations).</summary>
    internal void ShowRelations() => ShowTab(DiploTab.Relations);

    /// <summary>Opens an intention on its forms, as a click on it does (the captures of --deals=open).</summary>
    internal void Intend(Intent i)
    {
        diploIntent = i;
        RefreshDiplomacy();
    }

    /// <summary>One form an intention takes: what the player proposes, and its terms.</summary>
    sealed record Form(string Label, string Tip, DealKind Kind, Side? Target = null, int? Planet = null, double Amount = 0, bool PerMinute = false);

    /// <summary>A line of the thread: said by `Speaker` (null: what happened), or an offer made to the player, answered there while it holds.</summary>
    sealed record Line(Side? Speaker, string Text, DealOffered? Offer = null);

    /// <summary>The screen's layer while it is open; the faction it shows, its tab, the intention open.</summary>
    Control? diplomacy;
    Side? diploSide;
    DiploTab diploTab;
    Intent diploIntent;
    VBoxContainer diploList = null!, diploHead = null!, diploActions = null!, diploThread = null!, diploHistory = null!;
    Control diploTalk = null!, diploRelations = null!;
    GridContainer diploMatrix = null!;
    Label diploReputation = null!;
    Button diploTalkTab = null!, diploRelationsTab = null!;
    ScrollContainer diploScroll = null!, threadScroll = null!;
    /// <summary>The thread's lines as last shown, and refreshes left to keep it scrolled to the newest.</summary>
    int threadSeen = -1, threadDown;
    /// <summary>The exchange being put together: open or not, a planet or a tech, and its price in credits.</summary>
    bool swapOpen;
    int? swapPlanet;
    TechId? swapTech;
    double swapPrice;
    /// <summary>The screen stopped time, and gives it back when it closes.</summary>
    bool heldByDiplomacy;
    /// <summary>The matrix as last drawn: rebuilt when a relation changes.</summary>
    string diploMatrixKey = "";
    /// <summary>What was said with each faction since the campaign was opened here, the oldest first (TalkMax at most).</summary>
    readonly Dictionary<Side, List<Line>> talks = [];
    /// <summary>The AIs the player just proposed a deal to: a deal signed next is their yes.</summary>
    readonly HashSet<Side> awaiting = [];
    const int TalkMax = 40;

    public bool DiplomacyOpen => diplomacy is not null;

    /// <summary>Opens the screen, or closes it if it is open.</summary>
    public void ToggleDiplomacy()
    {
        if (diplomacy is null) OpenDiplomacy();
        else CloseDiplomacy();
    }

    /// <summary>Opens the screen, on `rival` if given, else on the faction shown last or the first one.</summary>
    public void OpenDiplomacy(Side? rival = null)
    {
        if (rival is Side r) SelectRival(r);
        if (diplomacy is not null)
        {
            RefreshDiplomacy();
            return;
        }
        CloseDrawer();
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var shade = Ignore(new ColorRect { Color = Palette.Bg with { A = 0.82f } });
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(shade);
        var margin = Ignore(new MarginContainer());
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var (side, px) in new[] { ("left", 16), ("right", 16), ("top", 64), ("bottom", 16) }) margin.AddThemeConstantOverride($"margin_{side}", px);
        layer.AddChild(margin);
        var panel = Panel(16, 14);
        margin.AddChild(panel);
        var col = Column(12);
        panel.AddChild(col);
        var head = Row(10);
        head.AddChild(Text("Diplomatie", size: 22, bold: true));
        diploReputation = Text("", Palette.Muted, 14);
        diploReputation.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        diploReputation.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(diploReputation);
        diploTalkTab = Small("Dialogue", () => ShowTab(DiploTab.Talk), "Ce que tu proposes à ce camp, et ce qu'il te répond");
        diploRelationsTab = Small("Relations et historique", () => ShowTab(DiploTab.Relations), "Où en sont tous les camps entre eux, et ce qui s'est passé avec celui-ci");
        head.AddChild(diploTalkTab);
        head.AddChild(diploRelationsTab);
        var close = Button("×", () => CloseDiplomacy(), "Fermer (Échap)", height: 32);
        close.CustomMinimumSize = new Vector2(32, 32);
        head.AddChild(close);
        col.AddChild(head);
        var body = Row(14);
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        col.AddChild(body);
        diploList = Column(6);
        var listScroll = Scroll(diploList);
        listScroll.CustomMinimumSize = new Vector2(220, 0);
        listScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(listScroll);
        // The dialogue: who it is and how it feels; what to tell it; what was said.
        var talk = Column(12);
        talk.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        talk.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        diploHead = Column(6);
        talk.AddChild(diploHead);
        var split = Row(16);
        split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        talk.AddChild(split);
        diploActions = Column(8);
        diploScroll = Scroll(diploActions);
        diploScroll.CustomMinimumSize = new Vector2(400, 0);
        diploScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        split.AddChild(diploScroll);
        var threadBox = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        threadBox.AddThemeStyleboxOverride("panel", UiTheme.Box(Palette.Bg with { A = 0.6f }, Palette.Line, 12));
        threadBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        threadBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        diploThread = Column(12);
        threadScroll = Scroll(diploThread);
        threadBox.AddChild(threadScroll);
        split.AddChild(threadBox);
        body.AddChild(talk);
        diploTalk = talk;
        // The second tab: every faction with every other, and the chronicle with the one chosen.
        var relations = Column(10);
        relations.AddChild(Text("Relations entre les camps", Palette.Accent, 15, bold: true));
        diploMatrix = new GridContainer();
        diploMatrix.AddThemeConstantOverride("h_separation", 14);
        diploMatrix.AddThemeConstantOverride("v_separation", 4);
        relations.AddChild(diploMatrix);
        diploHistory = Column(6);
        relations.AddChild(diploHistory);
        var relationsScroll = Scroll(relations);
        relationsScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        relationsScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(relationsScroll);
        diploRelations = relationsScroll;
        // Under the dialogs, which come on top of it (an offer, a betrayal to confirm).
        root.AddChild(layer);
        if (modal is not null) root.MoveChild(layer, modal.GetIndex());
        FadeIn(layer);
        Sfx.Play(Sound.Open);
        diplomacy = layer;
        diploMatrixKey = "";
        threadSeen = -1;
        if (scene.Opts.Net is null && !scene.Paused)
        {
            scene.TogglePause();
            heldByDiplomacy = true;
        }
        foreach (var (b, d) in drawerButtons) Outline(b, d == Drawer.Diplomacy ? Palette.Accent : null);
        diploSide ??= DiploRivals().FirstOrDefault(s => !G.Factions[s].Eliminated);
        RefreshDiplomacy();
    }

    /// <summary>Closes the screen; false if it was not open.</summary>
    public bool CloseDiplomacy()
    {
        if (diplomacy is null) return false;
        Sfx.Play(Sound.Close);
        diplomacy.QueueFree();
        diplomacy = null;
        foreach (var (b, d) in drawerButtons) Outline(b, d == drawer ? Palette.Accent : null);
        if (heldByDiplomacy)
        {
            heldByDiplomacy = false;
            if (scene.Paused) scene.TogglePause();
        }
        return true;
    }

    /// <summary>Every faction but the player and its teammates, who need no deal: the living first.</summary>
    List<Side> DiploRivals() => [.. G.Factions.Values.Where(f => !Galaxies.Teammates(G, f.Id, Player)).OrderBy(f => f.Eliminated).Select(f => f.Id)];

    void SelectRival(Side rival)
    {
        if (diploSide == rival) return;
        diploSide = rival;
        diploIntent = Intent.None;
        (swapOpen, swapPlanet, swapTech) = (false, null, null);
        threadSeen = -1;
        diploScroll?.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0);
    }

    void ShowTab(DiploTab tab)
    {
        diploTab = tab;
        threadSeen = -1;
        RefreshDiplomacy();
    }

    void RefreshDiplomacy()
    {
        if (diplomacy is null) return;
        var me = G.Factions[Player];
        diploReputation.Text = $"Ta réputation : {Signed(me.Reputation)} ({Repute(me.Reputation)})";
        Outline(diploTalkTab, diploTab == DiploTab.Talk ? Palette.Accent : null);
        Outline(diploRelationsTab, diploTab == DiploTab.Relations ? Palette.Accent : null);
        Sync(diploList, [.. DiploRivals().Select(RivalPart)]);
        diploTalk.Visible = diploTab == DiploTab.Talk;
        diploRelations.Visible = diploTab == DiploTab.Relations;
        var shown = diploSide is Side s && G.Factions.ContainsKey(s) ? s : (Side?)null;
        if (diploTab == DiploTab.Relations)
        {
            RefreshMatrix();
            Sync(diploHistory, shown is Side h ? [SectionPart($"Historique avec {NameOf(h)}"), HistoryPart(h)] : []);
            return;
        }
        if (shown is not Side r)
        {
            Sync(diploHead, [TextPart("Aucun autre camp.", Palette.Muted)]);
            Sync(diploActions, []);
            Sync(diploThread, []);
            return;
        }
        Sync(diploHead, HeadParts(r));
        Sync(diploActions, ActionParts(r));
        var thread = ThreadParts(r);
        Sync(diploThread, thread);
        // A line more: the newest is shown, once the thread is laid out again.
        if (thread.Count != threadSeen) (threadSeen, threadDown) = (thread.Count, 3);
        if (threadDown > 0)
        {
            threadDown--;
            threadScroll.ScrollVertical = (int)threadScroll.GetVScrollBar().MaxValue;
        }
    }

    static string Signed(double v) => v >= 0.5 ? $"+{N(Js.Round(v))}" : N(Js.Round(v));

    /// <summary>A reputation in a word.</summary>
    static string Repute(double r) => r >= 30 ? "très fiable" : r >= 8 ? "fiable" : r > -8 ? "neutre" : r > -30 ? "peu fiable" : "parjure";

    static Part SectionPart(string title) => new($"section:{title}", () => Text(title, Palette.Accent, 15, bold: true));

    /// <summary>
    /// A label the view translates itself (Lang.T): what it shows is made after translating, quoted
    /// or lowered. Its text comes once it is so: a label translates the text it is made with.
    /// </summary>
    static Label Own(Label l, string text = "")
    {
        l.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
        l.Text = text;
        return l;
    }

    /// <summary>Words said, in the language shown and its quotes.</summary>
    static string Quote(string french) => Lang.Current == Lang.English ? $"“{Lang.T(french)}”" : $"« {french} »";

    // ── The table of relations ────────────────────────────────────────────────────────

    /// <summary>Where `a` and `b` stand, in a word and its color: allies, at peace, at war.</summary>
    (string Word, Color Color) Standing(Side a, Side b) =>
        Galaxies.Allied(G, a, b) || Galaxies.Teammates(G, a, b) ? ("alliés", Palette.Ok)
        : Galaxies.TruceOf(G, a, b) is { Lasting: true } ? ("paix", Palette.Ok)
        : Galaxies.TruceOf(G, a, b) is { Opening: true } ? ("paix", Palette.Accent)
        : Galaxies.AtPeace(G, a, b) ? ("trêve", Palette.Accent)
        : ("guerre", Palette.Danger);

    /// <summary>What else binds `a` and `b`: trade, shared vision, a joint war, a tribute; a war `a` swore against `b`.</summary>
    string Extras(Side a, Side b)
    {
        var g = G;
        var bits = new List<string>();
        if (Galaxies.PactOf(g, DealKind.Trade, a, b) is not null) bits.Add("commerce");
        if (Galaxies.PactOf(g, DealKind.Vision, a, b) is not null) bits.Add("vision");
        if (Galaxies.JointWarOf(g, a, b) is { Target: Side t }) bits.Add($"guerre commune contre {NameOf(t)}");
        if (Galaxies.TributeOf(g, a, b) is not null) bits.Add("tribut");
        if (Galaxies.Sworn(g, a, b)) bits.Add("guerre jurée");
        return string.Join(", ", bits);
    }

    void RefreshMatrix()
    {
        var sides = G.Factions.Values.Where(f => !f.Eliminated).Select(f => f.Id).ToList();
        var key = string.Join(";", sides.SelectMany(a => sides.Where(b => b.Id > a.Id).Select(b => $"{Standing(a, b).Word}{Extras(a, b)}{Extras(b, a)}")));
        key = string.Join(",", sides) + "|" + key;
        if (key == diploMatrixKey) return;
        diploMatrixKey = key;
        foreach (var c in diploMatrix.GetChildren())
        {
            diploMatrix.RemoveChild(c);
            c.QueueFree();
        }
        diploMatrix.Columns = sides.Count + 1;
        diploMatrix.AddChild(Ignore(new Control()));
        foreach (var s in sides) diploMatrix.AddChild(SideTag(s));
        foreach (var a in sides)
        {
            diploMatrix.AddChild(SideTag(a));
            foreach (var b in sides)
            {
                if (a == b)
                {
                    diploMatrix.AddChild(Text("—", Palette.Muted, 13));
                    continue;
                }
                var (word, color) = Standing(a, b);
                var cell = Column(0);
                cell.AddChild(Text(word, color, 13, bold: true));
                var more = Extras(a, b);
                if (more.Length > 0) cell.AddChild(Text(more, Palette.Muted, 12, wrap: true));
                cell.CustomMinimumSize = new Vector2(96, 0);
                diploMatrix.AddChild(cell);
            }
        }
    }

    /// <summary>A faction's emblem and short name, for the table.</summary>
    HBoxContainer SideTag(Side s)
    {
        var row = Row(6);
        row.AddChild(Emblem(s));
        row.AddChild(Text(s == Player ? "Toi" : NameOf(s), s == Player ? Palette.Accent : Palette.Text, 13, bold: true));
        return row;
    }

    // ── The list of factions ──────────────────────────────────────────────────────────

    /// <summary>What a faction's line shows, keyed so that it is rebuilt when that changes: its standing, its offers to the player, whether it is shown.</summary>
    Part RivalPart(Side r)
    {
        var f = G.Factions[r];
        var offers = G.Offers.Count(o => o.From == r && o.To == Player);
        var ultimatum = G.Offers.Exists(o => o.From == r && o.To == Player && o.Kind == DealKind.Ultimatum);
        var key = $"rival:{r}:{Standing(Player, r).Word}:{f.Eliminated}:{offers}:{ultimatum}:{diploSide == r}";
        return new Part(key, () =>
        {
            var b = new LabelButton(true, 10, 6) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MinSize = new Vector2(0, 52) };
            b.Add(NameOf(r), Palette.Text, 15);
            b.Add("", Palette.Muted, 12);
            b.Add("", ultimatum ? Palette.Danger : Palette.Accent, 12);
            b.Pressed += () =>
            {
                SelectRival(r);
                RefreshDiplomacy();
            };
            b.Marked = diploSide == r;
            if (ultimatum) Outline(b, Palette.Danger, false);
            else if (offers > 0 || diploSide == r) Outline(b, Palette.Accent, false);
            b.Paint();
            return b;
        }, c =>
        {
            var b = (LabelButton)c;
            var (word, _) = Standing(Player, r);
            b.Line(1).Text = f.Eliminated ? "éliminée" : f.AI is not null ? $"{word}, confiance {Signed(Galaxies.TrustOf(G, r, Player))}" : $"{word}, joueur";
            // Its own line: the view translates whole texts, and a longer one would be read as the shorter's.
            var news = b.Line(2);
            news.Text = ultimatum ? "Ultimatum !" : offers > 1 ? $"{offers} offres en attente" : offers == 1 ? "Une offre en attente" : "";
            news.Visible = offers > 0;
        });
    }

    // ── Who it is, and how it feels ──────────────────────────────────────────────────

    List<Part> HeadParts(Side r)
    {
        var f = G.Factions[r];
        var parts = new List<Part> { HeadPart(r) };
        if (f.Eliminated) parts.Add(TextPart("Ce camp a été éliminé.", Palette.Muted));
        else if (f.AI is not null) parts.Add(MoodPart(r));
        else parts.Add(TextPart("Un joueur : il répond lui-même à tes offres, dans la minute.", Palette.Muted, 14));
        return parts;
    }

    /// <summary>Its emblem, name, what it is (an AI's personality, or a player), and its reputation.</summary>
    Part HeadPart(Side r) => new($"head:{r}", () =>
    {
        var row = Row(14);
        row.AddChild(Heading(NameOf(r), r, 22));
        var what = Text("", Palette.Muted, 14, wrap: true);
        what.Name = "what";
        row.AddChild(what);
        return row;
    }, c =>
    {
        var f = G.Factions[r];
        var kind = f.AI is { } ai ? $"IA {Personalities.Defs[ai.Personality].Name.ToLowerInvariant()}, {Worlds.Difficulties[ai.Difficulty].Name.ToLowerInvariant()}" : "Joueur";
        ((Label)c.FindChild("what", true, false)).Text = $"{kind} ; réputation {Signed(f.Reputation)} ({Repute(f.Reputation)})"
            + (f.Weariness >= 1 ? $" ; lassitude de guerre {N(Math.Round(f.Weariness))}, revenu −{HudKit.Percent(Galaxies.WearyCost * f.Weariness / Galaxies.WearyMax)}" : "");
    });

    /// <summary>An AI's mood towards the player in a word, its reason in its own words, and its trust.</summary>
    Part MoodPart(Side r) => new($"mood:{r}", () =>
    {
        var col = Column(6);
        var line = Row(12);
        line.AddChild(Text("", size: 16, bold: true));
        line.AddChild(Own(Text("", Palette.Text, 15, wrap: true)));
        col.AddChild(line);
        var gauge = Row(12);
        var bar = new ProgressBar { ShowPercentage = false, MaxValue = 1, Step = 0, CustomMinimumSize = new Vector2(0, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var fill = UiTheme.Box(Palette.Ok, Palette.Ok, pad: 0);
        fill.SetContentMarginAll(0);
        var bg = UiTheme.Box(Palette.Danger with { A = 0.35f }, Palette.Danger with { A = 0.35f }, pad: 0);
        bg.SetContentMarginAll(0);
        bar.AddThemeStyleboxOverride("fill", fill);
        bar.AddThemeStyleboxOverride("background", bg);
        gauge.AddChild(bar);
        var trust = Text("", Palette.Muted, 14);
        trust.CustomMinimumSize = new Vector2(150, 0);
        gauge.AddChild(trust);
        col.AddChild(gauge);
        return col;
    }, c =>
    {
        var (mood, cause, weight) = GalaxyAi.Attitude(G, r, Player);
        var line = c.GetChild(0);
        var word = line.GetChild<Label>(0);
        word.Text = MoodWord(mood);
        Paint(word, mood >= Mood.Warm ? Palette.Ok : mood <= Mood.Distrustful ? Palette.Danger : Palette.Text);
        line.GetChild<Label>(1).Text = Quote(Because(r, mood, cause, weight));
        var trust = Galaxies.TrustOf(G, r, Player);
        var gauge = c.GetChild(1);
        gauge.GetChild<ProgressBar>(0).Value = Math.Clamp((trust + 100) / 200, 0, 1);
        gauge.GetChild<Label>(1).Text = $"Confiance {Signed(trust)}";
    });

    static string MoodWord(Mood m) => m switch
    {
        Mood.Friendly => "Amicale",
        Mood.Warm => "Bienveillante",
        Mood.Reserved => "Réservée",
        Mood.Distrustful => "Méfiante",
        _ => "Hostile",
    };

    /// <summary>
    /// Why an AI feels as it does, in its words: the faction it sees dominating, else what moved its
    /// trust the most, else the player's word as all know it, else its mood, or its nature when it
    /// has none yet.
    /// </summary>
    string Because(Side r, Mood mood, TrustCause? cause, double weight)
    {
        if (mood <= Mood.Reserved && PreviewOf(r)?.Peace == DealWhy.Leader) return "Tu deviens trop puissant.";
        if (cause is TrustCause c) return Said(c, weight > 0);
        var word = G.Factions[Player].Reputation;
        if (word <= -8) return "On dit que ta parole ne vaut rien.";
        if (word >= 8) return "On dit que tu tiens parole.";
        if (mood >= Mood.Warm) return "Nous t'estimons.";
        if (mood <= Mood.Distrustful) return "Nous nous méfions de toi.";
        return G.Factions[r].AI!.Personality switch
        {
            Personality.Aggressive => "Je ne respecte que la force.",
            Personality.Defensive => "Nous restons sur nos gardes.",
            Personality.Economic => "Tout se négocie.",
            Personality.Technological => "Données insuffisantes à ton sujet.",
            _ => "Nous ne te connaissons pas encore.",
        };
    }

    /// <summary>What an AI says of what moved its trust, for better (`good`) or worse.</summary>
    static string Said(TrustCause c, bool good) => (c, good) switch
    {
        (TrustCause.Peace, _) => "La paix entre nous tient bon.",
        (TrustCause.Trade, true) => "Nos échanges nous profitent.",
        (TrustCause.Trade, false) => "Tu as rompu nos échanges.",
        (TrustCause.Alliance, _) => "Notre alliance tient.",
        (TrustCause.JointWar, _) => "Nous avons combattu côte à côte.",
        (TrustCause.Gifts, _) => "Tes présents ne sont pas oubliés.",
        (TrustCause.Demands, _) => "Tu nous as fait payer tribut.",
        (TrustCause.Attacks, _) => "Tu nous as attaqués.",
        (TrustCause.Betrayal, _) => "Tu as trahi ta parole.",
        (TrustCause.Witness, _) => "Nous t'avons vu trahir les autres.",
        (TrustCause.Help, true) => "Tu es venu à notre aide.",
        (TrustCause.Help, false) => "Tu nous as laissés seuls.",
        _ => "Tes menaces ne s'oublient pas.",
    };

    /// <summary>What an AI would answer the player, worked out here on the host, sent by it to a replica; null for a player.</summary>
    DealPreview? PreviewOf(Side r) => G.Factions[r].AI is { } ai ? ai.Preview ?? GalaxyAi.Mind(G, r, Player) : null;

    // ── What to tell it ──────────────────────────────────────────────────────────────

    List<Part> ActionParts(Side r)
    {
        var parts = new List<Part>();
        if (G.Factions[r].Eliminated) return parts;
        parts.Add(SectionPart("Tes intentions"));
        foreach (var i in (ReadOnlySpan<Intent>)[Intent.Peace, Intent.Trade, Intent.Ally, Intent.Demand])
        {
            parts.Add(IntentPart(r, i));
            if (i == Intent.Trade && diploIntent == Intent.Trade && swapOpen && !Galaxies.AtWar(G, Player, r)) parts.Add(ExchangePart(r));
        }
        parts.Add(WarPart(r));
        parts.Add(SectionPart("Accords en cours"));
        parts.Add(AccordsPart(r));
        return parts;
    }

    static string IntentName(Intent i) => i switch
    {
        Intent.Peace => "Faire la paix",
        Intent.Trade => "Commercer",
        Intent.Ally => "S'allier",
        _ => "Exiger, menacer",
    };

    /// <summary>The forms an intention takes with `r`: the deals it may propose.</summary>
    List<Form> FormsOf(Side r, Intent i)
    {
        var g = G;
        switch (i)
        {
            case Intent.Peace:
                return [new($"Trêve de {Minutes(Galaxies.TruceTime)}", "Aucun combat entre vous pendant ce temps, ni flotte envoyée chez l'autre", DealKind.Truce),
                    new("Paix durable", $"Une paix sans fin, qui ne se rompt pas : on la dénonce, et la guerre vient {Minutes(Galaxies.NoticeTime)} après", DealKind.Peace)];
            case Intent.Trade:
                return [new($"Accord commercial, +{N(Math.Round(Galaxies.TradeRate(g, Player, r), 1))}/s chacun", "Des crédits pour vous deux tant que dure la paix", DealKind.Trade),
                    new("Vision partagée", "Chacun voit ce que voit l'autre, sans alliance ; la guerre y met fin", DealKind.Vision)];
            case Intent.Ally:
                var forms = new List<Form> { new("Alliance", "Vision partagée, aucun combat, victoire commune ; la rompre est une trahison", DealKind.Alliance) };
                foreach (var t in g.Factions.Values.Where(t => !t.Eliminated && t.Id != r && t.Id != Player).Select(t => t.Id))
                {
                    forms.Add(new($"Guerre commune contre {NameOf(t)}",
                        $"{Minutes(Galaxies.JointWarTime)} de guerre menée ensemble ; faire la paix avec {NameOf(t)} pendant ce temps est une trahison", DealKind.JointWar, t));
                }
                return forms;
            default:
                var demand = Math.Max(30, Math.Round(Galaxies.IncomeOf(g, r) * 60 * 0.25 / 10) * 10);
                var ransom = Math.Max(200, Math.Round(Galaxies.IncomeOf(g, r) * 120 / 10) * 10);
                var threats = new List<Form>
                {
                    new($"Exiger {Credits(demand, true)}", "Un tribut contre une trêve : elle paie si elle te craint", DealKind.Demand, Amount: demand, PerMinute: true),
                    new($"Ultimatum : {Credits(ransom, false)}", $"Payer, ou la guerre : elle a {Left(Galaxies.UltimatumTime)} pour répondre. Ta réputation en souffre", DealKind.Ultimatum, Amount: ransom),
                };
                foreach (var p in g.Planets.Where(p => Galaxies.CanDemandPlanet(g, Player, r, p.Id).Ok))
                    threats.Add(new($"Ultimatum : {p.Name}", $"Céder {p.Name}, ou la guerre ; elle ne cède qu'une planète secondaire pour elle. Ta réputation en souffre", DealKind.Ultimatum, Planet: p.Id));
                return threats;
        }
    }

    Result CanPropose(Side r, Form f) => Galaxies.CanPropose(G, Player, r, f.Kind, f.Amount, f.PerMinute, f.Target, f.Planet);

    /// <summary>How the AI `r` leans towards a form, from what it would answer; null for a player, who answers for themselves.</summary>
    Leaning? LeaningOf(Side r, Form f)
    {
        if (PreviewOf(r) is not { } p) return null;
        var why = f.Kind switch
        {
            DealKind.Truce => p.Truce,
            DealKind.Peace => p.Peace,
            DealKind.Trade => p.Trade,
            DealKind.Vision => p.Vision,
            DealKind.Alliance => p.Alliance,
            DealKind.JointWar => p.JointWar.GetValueOrDefault(f.Target!.Value, DealWhy.NoThreat),
            DealKind.Demand => p.DemandLimit <= 0 ? DealWhy.NotAfraid : f.Amount <= p.DemandLimit ? DealWhy.Yes : DealWhy.NoMeans,
            _ => !p.Cedes ? DealWhy.NotAfraid : f.Planet is not null || f.Amount <= p.YieldLimit ? DealWhy.Yes : DealWhy.NoMeans,
        };
        return GalaxyAi.LeaningOf(G, r, Player, f.Kind, why);
    }

    /// <summary>A leaning in a word and its color; a threat says whether it is feared.</summary>
    static (string Text, Color Color) LeaningWord(Leaning? l, bool threat) => (l, threat) switch
    {
        (null, _) => ("", Palette.Muted),
        (Leaning.Open, true) => ("elle te craint", Palette.Ok),
        (_, true) => ("elle ne te craint pas", Palette.Danger),
        (Leaning.Open, _) => ("ouverte", Palette.Ok),
        (Leaning.Reluctant, _) => ("réticente", Palette.Accent),
        _ => ("hostile", Palette.Danger),
    };

    /// <summary>
    /// An intention: its button, how the AI leans towards the best of its forms (or why none is
    /// possible), and once open, its forms, each proposed in one click, with how the AI leans
    /// towards it or why it cannot go.
    /// </summary>
    Part IntentPart(Side r, Intent i)
    {
        var forms = FormsOf(r, i);
        var open = diploIntent == i;
        var threat = i == Intent.Demand;
        var key = $"intent:{r}:{i}:{open}:{(open && swapOpen)}:{string.Join("|", forms.Select(f => f.Label))}";
        return new Part(key, () =>
        {
            var col = Column(6);
            var row = Row(12);
            var b = Small(IntentName(i), () =>
            {
                diploIntent = open ? Intent.None : i;
                swapOpen = false;
                RefreshDiplomacy();
            });
            b.CustomMinimumSize = new Vector2(190, 36);
            b.Alignment = HorizontalAlignment.Left;
            Outline(b, open ? Palette.Accent : null);
            row.AddChild(b);
            row.AddChild(Own(Text("", Palette.Muted, 14, wrap: true)));
            col.AddChild(row);
            if (!open) return col;
            var indent = Ignore(new MarginContainer());
            indent.AddThemeConstantOverride("margin_left", 20);
            var box = Column(4);
            indent.AddChild(box);
            foreach (var f in forms)
            {
                var line = Row(10);
                var send = Small(f.Label, () => Propose(r, f.Kind, f.Amount, f.PerMinute, f.Target, f.Planet), f.Tip);
                line.AddChild(send);
                line.AddChild(Own(Text("", Palette.Muted, 13, wrap: true)));
                box.AddChild(line);
            }
            if (i == Intent.Trade)
            {
                var swap = Small("Échanger une planète ou une technologie", () =>
                {
                    swapOpen = !swapOpen;
                    (swapPlanet, swapTech) = (null, null);
                    RefreshDiplomacy();
                }, "Une planète ou un niveau de technologie contre des crédits, ou en cadeau");
                swap.Name = "swap";
                Outline(swap, swapOpen ? Palette.Accent : null);
                swap.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
                box.AddChild(swap);
            }
            col.AddChild(indent);
            return col;
        }, c =>
        {
            var hint = c.GetChild(0).GetChild<Label>(1);
            var cans = forms.Select(f => CanPropose(r, f)).ToList();
            var possible = forms.Where((_, k) => cans[k].Ok).ToList();
            var (text, color) = possible.Count == 0 ? (forms.Count > 0 ? cans[0].Reason : "", Palette.Muted)
                : LeaningWord(possible.Select(f => LeaningOf(r, f)).Min(), threat);
            hint.Text = Lower(Lang.T(text));
            Paint(hint, color);
            if (!open) return;
            var box = c.GetChild(1).GetChild(0);
            for (var k = 0; k < forms.Count; k++)
            {
                var line = box.GetChild(k);
                line.GetChild<Button>(0).Disabled = !cans[k].Ok;
                var l = line.GetChild<Label>(1);
                var (t, tc) = !cans[k].Ok ? (cans[k].Reason, Palette.Muted) : LeaningWord(LeaningOf(r, forms[k]), threat);
                l.Text = Lower(Lang.T(t));
                Paint(l, tc);
            }
            if (box.FindChild("swap", false, false) is Button swap)
            {
                var war = Galaxies.AtWar(G, Player, r);
                swap.Disabled = war;
                swap.TooltipText = war ? "Il faut d'abord la paix" : "Une planète ou un niveau de technologie contre des crédits, ou en cadeau";
            }
        });
    }

    /// <summary>
    /// The fifth intention: ending the peace there is, named after it (breaking a truce or an
    /// alliance, denouncing a lasting peace), with what it brings; nothing to end at war, nor in
    /// the opening peace, which nobody breaks.
    /// </summary>
    Part WarPart(Side r)
    {
        var g = G;
        var truce = Galaxies.TruceOf(g, Player, r);
        (string Label, DealKind? Kind) end = Galaxies.Allied(g, Player, r) ? ("Rompre l'alliance", DealKind.Alliance)
            : truce is { Opening: false, Lasting: true } ? ("Dénoncer la paix", DealKind.Peace)
            : truce is { Opening: false } ? ("Rompre la trêve", DealKind.Truce)
            : ("Déclarer la guerre", null);
        var (label, kind) = end;
        return new Part($"war:{r}:{label}:{kind}", () =>
        {
            var row = Row(12);
            var b = Small(label, () =>
            {
                if (kind is DealKind k) ConfirmBreak(r, k);
            });
            b.CustomMinimumSize = new Vector2(190, 36);
            b.Alignment = HorizontalAlignment.Left;
            b.Disabled = kind is null;
            row.AddChild(b);
            row.AddChild(Text("", Palette.Muted, 14, wrap: true));
            return row;
        }, c =>
        {
            var hint = c.GetChild<Label>(1);
            var (text, color) = kind switch
            {
                DealKind.Alliance or DealKind.Truce => ("une trahison", Palette.Danger),
                DealKind.Peace => ($"la guerre {Minutes(Galaxies.NoticeTime)} après", Palette.Accent),
                _ => Galaxies.TruceOf(G, Player, r) is { Opening: true } t ? ($"paix de départ, encore {Left(t.Remaining)}", Palette.Muted) : ("déjà en guerre", Palette.Muted),
            };
            hint.Text = text;
            Paint(hint, color);
        });
    }

    /// <summary>
    /// The accords in force with `r`, each with how long it holds and how to end what may be ended
    /// apart; a truce, a lasting peace and an alliance end by the fifth intention.
    /// </summary>
    Part AccordsPart(Side r)
    {
        var g = G;
        var allied = Galaxies.Allied(g, Player, r);
        var truce = Galaxies.TruceOf(g, Player, r);
        var trade = Galaxies.PactOf(g, DealKind.Trade, Player, r) is not null;
        var vision = Galaxies.PactOf(g, DealKind.Vision, Player, r) is not null;
        var war = Galaxies.JointWarOf(g, Player, r);
        var tribute = Galaxies.TributeOf(g, Player, r);
        var promise = g.Promises.Find(p => p.Asker == r && p.Helper == Player);
        var key = $"accords:{r}:{allied}:{truce is not null}:{truce?.Opening}:{truce?.Lasting}:{trade}:{vision}:{war?.Target}:{tribute?.Payer}:{promise?.Planet}";
        return new Part(key, () =>
        {
            var col = Column(6);
            void Line(string name, string text, Action? end = null, string endText = "", string tip = "")
            {
                var row = Row(10);
                var l = Text(text, Palette.Text, 14, wrap: true);
                l.Name = name;
                l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(l);
                if (end is not null) row.AddChild(Small(endText, end, tip));
                col.AddChild(row);
            }
            if (allied) Line("alliance", "Alliance : vision partagée, aucun combat, victoire commune");
            if (truce is not null) Line("truce", "");
            if (trade) Line("trade", "", () => End(r, DealKind.Trade), "Arrêter");
            if (vision) Line("vision", "Vision partagée : chacun voit ce que voit l'autre", () => End(r, DealKind.Vision), "Arrêter", "Elle le prendra un peu mal");
            if (war is { Target: Side t }) Line("war", "", () => End(r, DealKind.JointWar), "Se retirer", $"La laisser seule contre {NameOf(t)} : elle t'en voudra");
            if (tribute is not null)
            {
                if (tribute.Payer == Player) Line("tribute", "", () => ConfirmBreak(r, DealKind.Tribute), "Cesser de payer", "Rompt la trêve que le tribut achète");
                else Line("tribute", "", () => End(r, DealKind.Tribute), "Lever", "Elle cesse de te payer ; la trêve tient");
            }
            if (promise is not null) Line("promise", "");
            if (col.GetChildCount() == 0) Line("none", Grudge(r) is var grudge && grudge > 0
                ? $"Aucun : en guerre. Trahie, elle refuse trêve et alliance encore {Left(grudge)}" : "Aucun : vous êtes en guerre");
            return col;
        }, c =>
        {
            void Set(string name, string text)
            {
                if (c.FindChild(name, true, false) is Label l) l.Text = text;
            }
            if (Galaxies.TruceOf(G, Player, r) is { } t) Set("truce", t.Opening ? $"Paix de départ : encore {Left(t.Remaining)}, nul ne peut la rompre{(t.Lasting ? " ; une paix durable lui succédera" : "")}"
                : t.Lasting ? $"Paix durable : sans fin ; la dénoncer laisse {Minutes(Galaxies.NoticeTime)} avant la guerre"
                : t.Renewed ? $"Trêve : encore {Left(t.Remaining)}, déjà prolongée" : $"Trêve : encore {Left(t.Remaining)}");
            if (Galaxies.JointWarOf(G, Player, r) is { Target: Side aim } w) Set("war", $"Guerre commune contre {NameOf(aim)} : encore {Left(w.Remaining)}");
            if (Galaxies.PactOf(G, DealKind.Trade, Player, r) is { } pact)
            {
                var age = G.Time - pact.Since;
                Set("trade", $"Commerce : +{N(Math.Round(Galaxies.TradeRate(G, Player, r), 1))}/s chacun"
                    + (age < Galaxies.TradeMaturity ? $", il grandit encore {Left(Galaxies.TradeMaturity - age)}" : ", à son plein"));
            }
            if (Galaxies.TributeOf(G, Player, r) is { } tr)
            {
                Set("tribute", tr.Payer == Player ? $"Tu lui paies {Credits(tr.PerMinute, true)}, encore {Left(tr.Remaining)}"
                    : $"Elle te paie {Credits(tr.PerMinute, true)}, encore {Left(tr.Remaining)}");
            }
            if (G.Promises.Find(p => p.Asker == r && p.Helper == Player) is { } p)
                Set("promise", $"Ta promesse : défendre {PlanetName(p.Planet)}, une flotte là-bas sous {Left(p.Remaining)}");
        });
    }

    /// <summary>
    /// Exchanges with a faction at peace: one of its planets or one of the player's (never a
    /// capital), or a level of a tech where one is ahead, for a price to pick around what it is
    /// worth (nothing: a gift). An AI answers at once, or names its price.
    /// </summary>
    Part ExchangePart(Side r)
    {
        var g = G;
        var (me, them) = (g.Factions[Player], g.Factions[r]);
        bool Tradable(Planet p) => p.Home != p.Owner && g.Planets.Count(o => o.Owner == p.Owner) > 1;
        List<int> Best(Side owner) => g.Planets.Where(p => p.Owner == owner && Tradable(p)).OrderByDescending(p => Galaxies.PlanetValue(g, p)).Take(6).Select(p => p.Id).ToList();
        var (mine, theirs) = (Best(Player), Best(r));
        var techs = Techs.All.Where(t => me.Research[t] != them.Research[t]).ToList();
        if (swapPlanet is int sp && !mine.Contains(sp) && !theirs.Contains(sp) || swapTech is TechId st && !techs.Contains(st)) (swapPlanet, swapTech) = (null, null);
        var key = $"exchange:{r}:{swapPlanet}:{swapTech}:{swapPrice}:{string.Join(",", mine)}:{string.Join(",", theirs)}:"
            + string.Join(",", techs.Select(t => $"{t}{me.Research[t]}{them.Research[t]}"));
        // What the chosen item is worth, and whether the player pays for it.
        var (worth, pays) = swapPlanet is int planet ? (Galaxies.PlanetValue(g, g.Planets[planet]), g.Planets[planet].Owner == r)
            : swapTech is TechId tech ? (Galaxies.TechValue(Math.Min(me.Research[tech], them.Research[tech]), tech), them.Research[tech] > me.Research[tech])
            : (0, false);
        var kind = swapPlanet is not null ? DealKind.Planet : DealKind.Tech;
        return new Part(key, () =>
        {
            var indent = Ignore(new MarginContainer());
            indent.AddThemeConstantOverride("margin_left", 40);
            var col = Column(8);
            indent.AddChild(col);
            var items = Flow(6);
            void Pick(string label, int? p, TechId? t, string tip)
            {
                var b = Small(label, () =>
                {
                    (swapPlanet, swapTech) = (p, t);
                    swapPrice = -1;
                    RefreshDiplomacy();
                }, tip);
                Outline(b, swapPlanet == p && swapTech == t ? Palette.Accent : null);
                items.AddChild(b);
            }
            foreach (var id in theirs) Pick($"Obtenir {PlanetName(id)}", id, null, $"Valeur estimée : {Credits(Galaxies.PlanetValue(g, g.Planets[id]), false)}");
            foreach (var id in mine) Pick($"Céder {PlanetName(id)}", id, null, $"Valeur estimée : {Credits(Galaxies.PlanetValue(g, g.Planets[id]), false)}");
            foreach (var t in techs)
            {
                var level = Math.Min(me.Research[t], them.Research[t]) + 1;
                Pick($"{(me.Research[t] > them.Research[t] ? "Transmettre" : "Recevoir")} {Techs.Defs[t].Name} {level}", null, t,
                    $"Valeur : {Credits(Galaxies.TechValue(level - 1, t), false)}");
            }
            if (items.GetChildCount() == 0) col.AddChild(Text("Rien à échanger pour l'instant. Une capitale ne se cède pas.", Palette.Muted, 13, wrap: true));
            col.AddChild(items);
            if (swapPlanet is null && swapTech is null) return indent;
            if (swapPrice < 0) swapPrice = Math.Round(worth / 50) * 50;
            var prices = Flow(6);
            foreach (var share in new[] { 0, 0.5, 1, 1.5, 2 })
            {
                var amount = Math.Round(worth * share / 50) * 50;
                var b = Small(amount <= 0 ? "Cadeau" : Credits(amount, false), () =>
                {
                    swapPrice = amount;
                    RefreshDiplomacy();
                }, share == 1 ? "Ce que ça vaut, à peu près" : amount <= 0 ? "Un cadeau réchauffe une IA" : "");
                Outline(b, amount == swapPrice ? Palette.Accent : null);
                prices.AddChild(b);
            }
            col.AddChild(prices);
            var send = Small(pays ? "Proposer d'acheter" : "Proposer de céder", () =>
            {
                Propose(r, kind, swapPrice, planet: swapPlanet, tech: swapTech);
                (swapPlanet, swapTech) = (null, null);
            }, "", primary: true);
            send.Name = "send";
            send.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            col.AddChild(send);
            return indent;
        }, c =>
        {
            if (c.FindChild("send", true, false) is not Button send) return;
            var can = Galaxies.CanPropose(G, Player, r, kind, swapPrice, planet: swapPlanet, tech: swapTech);
            send.Disabled = !can.Ok;
            send.TooltipText = can.Reason;
        });
    }

    // ── What was said ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The thread with `r`: what was said, the newest last; each offer it made the player that
    /// still holds, answered on its line (after the thread if it was made before the screen knew
    /// of it); the player's own offers to a player, waiting.
    /// </summary>
    List<Part> ThreadParts(Side r)
    {
        var lines = talks.GetValueOrDefault(r) ?? [];
        var pending = G.Offers.Where(o => o.From == r && o.To == Player).ToList();
        var parts = new List<Part>();
        for (var k = 0; k < lines.Count; k++)
        {
            // An offer is answered on the newest line that made one of its kind.
            DealOffer? live = null;
            if (lines[k].Offer is { } o && !lines.Skip(k + 1).Any(l => l.Offer?.Kind == o.Kind) && pending.Find(p => p.Kind == o.Kind) is { } still)
            {
                live = still;
                pending.Remove(still);
            }
            parts.Add(LinePart($"line:{r}:{k}:{live is not null}", lines[k], live));
        }
        foreach (var o in pending)
        {
            var offered = new DealOffered(o.From, o.To, o.Kind, o.Amount, o.PerMinute, o.Target, o.Planet, o.Terms, o.Tech, o.Counter);
            parts.Add(LinePart($"offer:{r}:{o.Kind}", new Line(r, "", offered), o));
        }
        foreach (var o in G.Offers.Where(o => o.From == Player && o.To == r))
            parts.Add(TextPart($"Ton offre ({Noun(o.Kind)}) attend sa réponse : encore {Left(o.Remaining)}", Palette.Muted, 13));
        if (parts.Count == 0) parts.Add(TextPart("Rien de neuf entre vous. Choisis une intention : sa réponse viendra ici.", Palette.Muted, 14));
        return parts;
    }

    /// <summary>A line of the thread: who says it and what, in quotes; an offer with its answers while it holds; what happened, muted.</summary>
    Part LinePart(string key, Line line, DealOffer? live) => new(key, () =>
    {
        var row = Row(10);
        var mine = line.Speaker == Player;
        var who = Text(line.Speaker is Side s ? (mine ? "Toi" : NameOf(s)) : "", mine ? Palette.Accent : Palette.Text, 14, bold: true);
        who.CustomMinimumSize = new Vector2(110, 0);
        who.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        row.AddChild(who);
        var col = Column(6);
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(col);
        if (line.Offer is not { } o)
        {
            col.AddChild(line.Speaker is null ? Text(line.Text, Palette.Muted, 14, wrap: true) : Own(Text("", Palette.Text, 14, wrap: true), Quote(line.Text)));
            return row;
        }
        var ultimatum = o.Kind == DealKind.Ultimatum;
        if (o.Counter) col.AddChild(Own(Text("", Palette.Text, 14, wrap: true), Quote(CounterText(o))));
        else
        {
            col.AddChild(Text(OfferTitle(o), ultimatum ? Palette.Danger : Palette.Text, 14, bold: true, wrap: true));
            col.AddChild(Text(OfferText(o), Palette.Muted, 13, wrap: true));
        }
        if (live is null) return row;
        var answers = Row(8);
        var yes = Small(ultimatum ? "Céder" : "Accepter", () => Answer(o.From, o.Kind, true), "", primary: true);
        yes.Name = "yes";
        answers.AddChild(yes);
        answers.AddChild(Small(ultimatum ? "Refuser : la guerre" : "Refuser", () => Answer(o.From, o.Kind, false)));
        var left = Text("", Palette.Muted, 13);
        left.Name = "left";
        answers.AddChild(left);
        col.AddChild(answers);
        return row;
    }, live is null ? null : c =>
    {
        if (G.Offers.Find(x => x.From == live.From && x.To == Player && x.Kind == live.Kind) is not { } now) return;
        ((Label)c.FindChild("left", true, false)).Text = now.Kind == DealKind.Ultimatum ? $"encore {Left(now.Remaining)}, sinon la guerre" : $"encore {Left(now.Remaining)}";
        // A counter-offer's price is paid at once.
        var yes = (Button)c.FindChild("yes", true, false);
        var broke = now.Counter && now.Kind is not (DealKind.Planet or DealKind.Tech) && G.Factions[Player].Credits < now.Amount;
        yes.Disabled = broke;
        yes.TooltipText = broke ? "Crédits insuffisants" : "";
    });

    /// <summary>What the player says, proposing a deal.</summary>
    string Proposal(Side to, DealKind kind, double amount, bool perMinute, Side? target, int? planet, TechId? tech) => kind switch
    {
        DealKind.Truce => $"Je te propose une trêve de {Minutes(Galaxies.TruceTime)}.",
        DealKind.Peace => "Je te propose une paix durable.",
        DealKind.Trade => "Je te propose un accord commercial.",
        DealKind.Vision => "Je te propose de partager la vision.",
        DealKind.Alliance => "Je te propose une alliance.",
        DealKind.JointWar => $"Faisons la guerre ensemble à {(target is Side t ? NameOf(t) : "?")}.",
        DealKind.Demand => $"Paie-moi {Credits(amount, perMinute)}, et la trêve est à toi.",
        DealKind.Ultimatum => planet is int p ? $"Cède-moi {PlanetName(p)}, ou c'est la guerre." : $"Paie-moi {Credits(amount, false)}, ou c'est la guerre.",
        DealKind.Planet when planet is int p => G.Planets[p].Owner == Player ? $"Je te cède {PlanetName(p)}{For(amount, true)}." : $"Cède-moi {PlanetName(p)}{For(amount)}.",
        DealKind.Tech when tech is TechId t => G.Factions[Player].Research[t] > G.Factions[to].Research[t]
            ? $"Je te transmets {Techs.Defs[t].Name}{For(amount, true)}." : $"Transmets-moi {Techs.Defs[t].Name}{For(amount)}.",
        _ => $"Je te propose {Noun(kind)}.",
    };

    /// <summary>What an AI says, offering the deal it would take instead of the one proposed.</summary>
    string CounterText(DealOffered o)
    {
        var price = Credits(o.Amount, false);
        return o.Kind switch
        {
            DealKind.Truce => $"Une trêve, oui, contre {price}.",
            DealKind.Peace => $"Une paix durable, oui, contre {price}.",
            DealKind.Alliance => $"Une alliance, oui, contre {price}.",
            DealKind.Trade => $"Un accord commercial, oui, contre {price}.",
            DealKind.Vision => $"Partager la vision, oui, contre {price}.",
            DealKind.JointWar => $"La guerre contre {(o.Target is Side t ? NameOf(t) : "?")}, oui, contre {price}.",
            DealKind.Planet when o.Planet is int p => G.Planets[p].Owner == o.From ? $"{PlanetName(p)}, oui, pour {price}." : $"Pour {PlanetName(p)}, nous t'en donnons {price}.",
            DealKind.Tech when o.Tech is TechId t => G.Factions[o.From].Research[t] > G.Factions[Player].Research[t]
                ? $"{Techs.Defs[t].Name}, oui, pour {price}." : $"Pour {Techs.Defs[t].Name}, nous t'en donnons {price}.",
            _ => $"Oui, contre {price}.",
        };
    }

    /// <summary>A line said with `rival`, kept with what was said before.</summary>
    void Talk(Side rival, Line line)
    {
        if (!talks.TryGetValue(rival, out var lines)) talks[rival] = lines = [];
        lines.Add(line);
        if (lines.Count > TalkMax) lines.RemoveRange(0, lines.Count - TalkMax);
    }

    /// <summary>
    /// What an event adds to the thread with the faction it concerns: an offer made to the player,
    /// a refusal of the player's offer with its reason, an AI's words; a deal signed, broken or
    /// ended, as the chronicle tells it, after an AI's yes to what the player just proposed.
    /// </summary>
    void Converse(GalaxyEvent ev)
    {
        var me = Player;
        switch (ev)
        {
            case DealOffered o:
                if (o.To != me) return;
                awaiting.Remove(o.From);
                Talk(o.From, new Line(o.From, "", o));
                return;
            case DealRefused r:
                if (r.From != me) return;
                awaiting.Remove(r.To);
                Talk(r.To, new Line(r.To, r.Reason.Length > 0 ? $"Non. {r.Reason}." : "Non."));
                return;
            case AiSaid s:
                if (s.To == me) Talk(s.From, new Line(s.From, RemarkText(s.From, s.What)));
                return;
        }
        if (Galaxies.Entry(ev) is not { } e || (e.A != me && e.B != me) || e.Kind is ChronicleKind.Refused or ChronicleKind.Said or ChronicleKind.Issued or ChronicleKind.Asked) return;
        var other = e.A == me ? e.B : e.A;
        // Whatever came of the proposal, it is answered; a deal signed is its yes.
        if (awaiting.Remove(other) && e.Kind == ChronicleKind.Signed) Talk(other, new Line(other, "D'accord."));
        e.Time = G.Time;
        Talk(other, new Line(null, Chronicled(e)));
    }

    // ── The chronicle ─────────────────────────────────────────────────────────────────

    /// <summary>The last things that passed between the player and `r`, the newest first.</summary>
    Part HistoryPart(Side r)
    {
        var lines = G.Chronicle.Where(e => (e.A == r && e.B == Player) || (e.A == Player && e.B == r)).TakeLast(30).Reverse().ToList();
        return new Part($"history:{r}:{lines.Count}:{(lines.Count > 0 ? lines[0].Time : 0)}", () =>
        {
            var col = Column(4);
            if (lines.Count == 0) col.AddChild(Text("Rien encore entre vous.", Palette.Muted, 13));
            foreach (var e in lines)
            {
                var row = Row(10);
                var when = Text(Minutes(e.Time), Palette.Muted, 13);
                when.CustomMinimumSize = new Vector2(64, 0);
                row.AddChild(when);
                if (e.Kind == ChronicleKind.Said) row.AddChild(Text(e.A == Player ? "Toi" : NameOf(e.A), Palette.Accent, 13, bold: true));
                var text = Text(Chronicled(e), e.Kind == ChronicleKind.Said ? Palette.Accent : Palette.Text, 13, wrap: true);
                text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(text);
                col.AddChild(row);
            }
            return col;
        });
    }

    /// <summary>A line of the chronicle in words, the player addressed as "tu".</summary>
    string Chronicled(DiploEntry e)
    {
        string Who(Side s) => s == Player ? "Toi" : NameOf(s);
        var me = Player;
        var by = e.By is Side b ? Who(b) : "";
        var mine = e.By == me;
        return e.Kind switch
        {
            ChronicleKind.Signed => e.Deal switch
            {
                DealKind.Truce => "Trêve signée",
                DealKind.Alliance => "Alliance signée",
                DealKind.Trade => "Accord commercial signé",
                DealKind.Vision => "Vision partagée",
                DealKind.JointWar => e.Extra >= 0 ? $"Guerre commune contre {NameOf(new Side(e.Extra))}" : "Guerre commune",
                DealKind.Peace => "Paix durable signée",
                DealKind.Planet => e.A == me ? $"Tu cèdes {PlanetName(e.Extra)} à {NameOf(e.B)}{Paid(e.Amount)}"
                    : e.B == me ? $"{NameOf(e.A)} te cède {PlanetName(e.Extra)}{Paid(e.Amount)}" : $"{NameOf(e.A)} cède {PlanetName(e.Extra)} à {NameOf(e.B)}{Paid(e.Amount)}",
                DealKind.Tech => e.A == me ? $"Tu transmets {Techs.Defs[(TechId)e.Extra].Name} à {NameOf(e.B)}{Paid(e.Amount)}"
                    : e.B == me ? $"{NameOf(e.A)} te transmet {Techs.Defs[(TechId)e.Extra].Name}{Paid(e.Amount)}"
                    : $"{NameOf(e.A)} transmet {Techs.Defs[(TechId)e.Extra].Name} à {NameOf(e.B)}{Paid(e.Amount)}",
                _ => e.A == me ? $"Tribut : tu paies {Credits(e.Amount, false)}" : e.B == me ? $"Tribut : {NameOf(e.A)} te paie {Credits(e.Amount, false)}"
                    : $"Tribut : {NameOf(e.A)} paie {Credits(e.Amount, false)}",
            },
            ChronicleKind.Expired => "Fin de la trêve, tenue jusqu'au bout",
            ChronicleKind.Broken => e.Deal switch
            {
                DealKind.Alliance => mine ? "Tu romps l'alliance" : $"{by} rompt l'alliance",
                DealKind.JointWar => mine ? "Tu trahis la guerre commune" : $"{by} trahit la guerre commune",
                _ => mine ? "Tu romps la trêve" : $"{by} rompt la trêve",
            },
            ChronicleKind.Ended => e.Deal switch
            {
                DealKind.Trade => "Fin du commerce",
                DealKind.Vision => "Fin de la vision partagée",
                DealKind.JointWar => "Fin de la guerre commune",
                DealKind.Peace => mine ? "Tu dénonces la paix durable" : $"{by} dénonce la paix durable",
                _ => "Fin du tribut",
            },
            ChronicleKind.Refused => mine ? $"Tu refuses {Noun(e.Deal)}" : $"{by} refuse {Noun(e.Deal)}",
            ChronicleKind.Defaulted => e.A == me ? "Tu cesses de payer le tribut" : $"{Who(e.A)} cesse de payer le tribut",
            ChronicleKind.Issued => e.A == me ? e.Extra >= 0 ? $"Ton ultimatum : {PlanetName(e.Extra)}" : $"Ton ultimatum : {Credits(e.Amount, false)}"
                : e.Extra >= 0 ? $"Ultimatum de {Who(e.A)} : {PlanetName(e.Extra)}" : $"Ultimatum de {Who(e.A)} : {Credits(e.Amount, false)}",
            ChronicleKind.Accepted => e.B == me ? "Tu cèdes à l'ultimatum" : $"{Who(e.B)} cède à l'ultimatum",
            ChronicleKind.Defied => e.B == me ? "Tu défies l'ultimatum" : $"{Who(e.B)} défie l'ultimatum",
            ChronicleKind.Ceded => e.A == me ? $"Tu cèdes {PlanetName(e.Extra)}" : $"{PlanetName(e.Extra)} cédée par {Who(e.A)}",
            ChronicleKind.Asked => e.Extra >= 0 ? $"{Who(e.A)} demande de l'aide pour {PlanetName(e.Extra)}" : $"{Who(e.A)} demande {Credits(e.Amount, false)}",
            ChronicleKind.Kept => e.B == me ? "Tu es venu à l'aide" : $"{Who(e.B)} est venu à l'aide",
            ChronicleKind.Failed => e.B == me ? "Tu n'es pas venu à l'aide" : $"{Who(e.B)} n'est pas venu à l'aide",
            // The speaker has a label of its own (HistoryPart): its words are translated whole.
            _ => RemarkText(e.A, (Remark)e.Extra),
        };
    }

    // ── An AI's words ────────────────────────────────────────────────────────────────

    /// <summary>What an AI says, in the words of its personality.</summary>
    string RemarkText(Side from, Remark what) =>
        Remarks.Of(G.Factions.TryGetValue(from, out var f) && f.AI is { } ai ? ai.Personality : Personality.Balanced, what);
}

/// <summary>An AI's words for each remark, by personality: the balanced one measured, the aggressive one blunt, the defensive one wary, the thrifty one counting, the technological one cold.</summary>
static class Remarks
{
    public static string Of(Personality p, Remark r) => (p, r) switch
    {
        (Personality.Aggressive, Remark.Warning) => "Ta faiblesse me tente. Prépare tes défenses.",
        (Personality.Aggressive, Remark.Threat) => "Obéis, ou je prendrai tout.",
        (Personality.Aggressive, Remark.Thanks) => "Un tribut bienvenu. Continue ainsi.",
        (Personality.Aggressive, Remark.Betrayed) => "Traître ! Tu paieras ce mépris.",
        (Personality.Aggressive, Remark.Welcome) => "Nos flottes réunies écraseront nos ennemis.",
        (Personality.Aggressive, Remark.Coalition) => "Abattons ce géant avant qu'il ne nous dévore.",
        (Personality.Aggressive, Remark.AskHelp) => "Viens te battre à nos côtés, si tu en as le courage.",
        (Personality.Aggressive, Remark.Grateful) => "Tu sais te battre. Je m'en souviendrai.",
        (Personality.Aggressive, Remark.Disappointed) => "Lâche. Je n'oublierai pas.",
        (Personality.Aggressive, Remark.Defiant) => "Viens donc prendre ce que tu exiges.",
        (Personality.Defensive, Remark.Warning) => "Nous ne pouvons plus garantir cette trêve. Retire tes flottes.",
        (Personality.Defensive, Remark.Threat) => "Notre patience a des limites. Accepte nos conditions.",
        (Personality.Defensive, Remark.Thanks) => "Ta générosité nous rassure.",
        (Personality.Defensive, Remark.Betrayed) => "Nous t'avions fait confiance. Plus jamais.",
        (Personality.Defensive, Remark.Welcome) => "Nos défenses sont désormais les tiennes.",
        (Personality.Defensive, Remark.Coalition) => "Seuls, nous tomberons. Ensemble, nous tiendrons.",
        (Personality.Defensive, Remark.AskHelp) => "Nos défenses cèdent. Viens vite, je t'en prie.",
        (Personality.Defensive, Remark.Grateful) => "Ton aide nous a sauvés.",
        (Personality.Defensive, Remark.Disappointed) => "Tu nous as laissés seuls face à l'ennemi.",
        (Personality.Defensive, Remark.Defiant) => "Nos murs tiendront. Nous refusons.",
        (Personality.Economic, Remark.Warning) => "Cette trêve ne nous rapporte plus rien. Elle prendra fin.",
        (Personality.Economic, Remark.Threat) => "Payer coûte moins cher qu'une guerre. Réfléchis bien.",
        (Personality.Economic, Remark.Thanks) => "Excellent placement. Nos échanges n'en seront que meilleurs.",
        (Personality.Economic, Remark.Betrayed) => "Une trahison se paie toujours, avec les intérêts.",
        (Personality.Economic, Remark.Welcome) => "Une alliance profitable pour nous deux.",
        (Personality.Economic, Remark.Coalition) => "Ce monopole nous ruine tous. Brisons-le ensemble.",
        (Personality.Economic, Remark.AskHelp) => "Nos caisses sont menacées. M'aider serait rentable pour toi aussi.",
        (Personality.Economic, Remark.Grateful) => "Investissement remboursé. Merci.",
        (Personality.Economic, Remark.Disappointed) => "Un partenaire peu fiable. C'est noté.",
        (Personality.Economic, Remark.Defiant) => "Ton prix est trop élevé. Refusé.",
        (Personality.Technological, Remark.Warning) => "Nos calculs sont formels : cette trêve doit cesser.",
        (Personality.Technological, Remark.Threat) => "Les probabilités sont contre toi. Accepte.",
        (Personality.Technological, Remark.Thanks) => "Contribution enregistrée. Merci.",
        (Personality.Technological, Remark.Betrayed) => "Trahison consignée. Nos systèmes ne l'oublieront pas.",
        (Personality.Technological, Remark.Welcome) => "Nos savoirs réunis feront la différence.",
        (Personality.Technological, Remark.Coalition) => "Ce camp déséquilibre la galaxie. Corrigeons cette anomalie ensemble.",
        (Personality.Technological, Remark.AskHelp) => "Nos défenses sont insuffisantes. Ton intervention est requise.",
        (Personality.Technological, Remark.Grateful) => "Ton intervention a été décisive.",
        (Personality.Technological, Remark.Disappointed) => "Tu n'es pas venu. Fiabilité révisée à la baisse.",
        (Personality.Technological, Remark.Defiant) => "Exigence rejetée. Nos modèles prévoient ta défaite.",
        (_, Remark.Warning) => "Nos intérêts divergent. Bientôt, notre trêve n'existera plus.",
        (_, Remark.Threat) => "Cède à ma demande, et la paix tiendra.",
        (_, Remark.Thanks) => "Ce geste ne sera pas oublié.",
        (_, Remark.Betrayed) => "Tu as trahi ta parole. Nous nous en souviendrons.",
        (_, Remark.Welcome) => "Ensemble, nous serons plus forts.",
        (_, Remark.Coalition) => "Ce camp devient trop puissant. Unissons-nous contre lui.",
        (_, Remark.AskHelp) => "Nos planètes sont menacées. Ton aide serait précieuse.",
        (_, Remark.Grateful) => "Tu as tenu parole. Merci.",
        (_, Remark.Disappointed) => "Nous comptions sur toi.",
        _ => "Nous ne plierons pas devant tes menaces.",
    };
}
