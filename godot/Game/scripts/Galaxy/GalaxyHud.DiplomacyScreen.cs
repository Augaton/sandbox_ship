// The diplomacy screen of the campaign (key T): where every faction stands with every other (a
// table of relations), the factions on the left, and on the right the one chosen: its attitude
// and why it trusts the player as it does, the accords in force and how to end them, the offers
// and demands it made, a proposal to build (several terms at once) with the answer it would get,
// the joint war, demand and ultimatum it may be made, and the chronicle of what passed between
// them. Alone, time stops while it is open. What an AI says comes in its personality's words.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Campaign;

public partial class GalaxyHud
{
    /// <summary>The screen's layer while it is open; the faction it shows.</summary>
    Control? diplomacy;
    Side? diploSide;
    VBoxContainer diploList = null!, diploDetail = null!;
    GridContainer diploMatrix = null!;
    Label diploReputation = null!;
    ScrollContainer diploScroll = null!;
    /// <summary>The proposal being built: its terms, and its tribute.</summary>
    DealTerms diploTerms;
    double diploAmount = 60;
    bool diploPerMinute = true;
    /// <summary>The screen stopped time, and gives it back when it closes.</summary>
    bool heldByDiplomacy;
    /// <summary>The matrix as last drawn: rebuilt when a relation changes.</summary>
    string diploMatrixKey = "";

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
        var col = Column(10);
        panel.AddChild(col);
        var head = Row(12);
        var title = Text("Diplomatie", size: 22, bold: true);
        head.AddChild(title);
        diploReputation = Text("", Palette.Muted, 14);
        diploReputation.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        diploReputation.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(diploReputation);
        var close = Button("×", () => CloseDiplomacy(), "Fermer (Échap)", height: 32);
        close.CustomMinimumSize = new Vector2(32, 32);
        head.AddChild(close);
        col.AddChild(head);
        col.AddChild(Text("Relations entre les camps", Palette.Muted, 13));
        diploMatrix = new GridContainer();
        diploMatrix.AddThemeConstantOverride("h_separation", 14);
        diploMatrix.AddThemeConstantOverride("v_separation", 4);
        col.AddChild(diploMatrix);
        var body = Row(14);
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        col.AddChild(body);
        diploList = Column(6);
        var listScroll = Scroll(diploList);
        listScroll.CustomMinimumSize = new Vector2(250, 0);
        listScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(listScroll);
        diploDetail = Column(12);
        diploDetail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        diploScroll = Scroll(diploDetail);
        diploScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        diploScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(diploScroll);
        // Under the dialogs, which come on top of it (an offer, a betrayal to confirm).
        root.AddChild(layer);
        if (modal is not null) root.MoveChild(layer, modal.GetIndex());
        FadeIn(layer);
        Sfx.Play(Sound.Open);
        diplomacy = layer;
        diploMatrixKey = "";
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
        diploTerms = DealTerms.None;
        diploAmount = 60;
        diploPerMinute = true;
        diploScroll?.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0);
    }


    void RefreshDiplomacy()
    {
        if (diplomacy is null) return;
        var me = G.Factions[Player];
        diploReputation.Text = $"Ta réputation : {Signed(me.Reputation)} ({Repute(me.Reputation)})";
        RefreshMatrix();
        Sync(diploList, [.. DiploRivals().Select(RivalPart)]);
        Sync(diploDetail, diploSide is Side r && G.Factions.ContainsKey(r) ? DetailParts(r) : [TextPart("Aucun autre camp.", Palette.Muted)]);
    }

    static string Signed(double v) => v >= 0.5 ? $"+{N(Js.Round(v))}" : N(Js.Round(v));

    /// <summary>A reputation in a word.</summary>
    static string Repute(double r) => r >= 30 ? "très fiable" : r >= 8 ? "fiable" : r > -8 ? "neutre" : r > -30 ? "peu fiable" : "parjure";

    // ── The table of relations ────────────────────────────────────────────────────────

    /// <summary>Where `a` and `b` stand, in a word and its color: allies, at peace, at war.</summary>
    (string Word, Color Color) Standing(Side a, Side b) =>
        Galaxies.Allied(G, a, b) || Galaxies.Teammates(G, a, b) ? ("alliés", Palette.Ok)
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
            b.Line(1).Text = f.Eliminated ? "éliminée" : f.AI is not null ? $"{word} · confiance {Signed(Galaxies.TrustOf(G, r, Player))}" : $"{word} · joueur";
            // Its own line: the view translates whole texts, and a longer one would be read as the shorter's.
            var news = b.Line(2);
            news.Text = ultimatum ? "Ultimatum !" : offers > 1 ? $"{offers} offres en attente" : offers == 1 ? "Une offre en attente" : "";
            news.Visible = offers > 0;
        });
    }

    // ── The faction chosen ───────────────────────────────────────────────────────────

    List<Part> DetailParts(Side r)
    {
        var g = G;
        var f = g.Factions[r];
        var parts = new List<Part> { HeadPart(r) };
        if (f.Eliminated)
        {
            parts.Add(TextPart("Ce camp a été éliminé.", Palette.Muted));
            parts.Add(HistoryPart(r));
            return parts;
        }
        if (f.AI is not null) parts.Add(AttitudePart(r));
        parts.Add(SectionPart("Accords en cours"));
        parts.Add(AccordsPart(r));
        var offers = g.Offers.Where(o => o.From == r && o.To == Player).ToList();
        if (offers.Count > 0)
        {
            parts.Add(SectionPart("Ses offres et demandes"));
            foreach (var o in offers) parts.Add(OfferPart(o));
        }
        foreach (var o in g.Offers.Where(o => o.From == Player && o.To == r)) parts.Add(TextPart($"Ton offre ({Noun(o.Kind)}) : réponse sous {Left(o.Remaining)}", Palette.Muted, 13));
        parts.Add(SectionPart("Proposer"));
        parts.Add(ProposalPart(r));
        parts.Add(SectionPart("Accords à part"));
        parts.Add(SpecialPart(r));
        parts.Add(SectionPart("Historique"));
        parts.Add(HistoryPart(r));
        return parts;
    }

    static Part SectionPart(string title) => new($"section:{title}", () => Text(title, Palette.Accent, 15, bold: true));

    /// <summary>Its emblem, name, what it is (an AI's personality, or a player), and its reputation.</summary>
    Part HeadPart(Side r) => new($"head:{r}", () =>
    {
        var col = Column(4);
        col.AddChild(Heading(NameOf(r), r, 22));
        var what = Text("", Palette.Muted, 14, wrap: true);
        what.Name = "what";
        col.AddChild(what);
        return col;
    }, c =>
    {
        var f = G.Factions[r];
        var kind = f.AI is { } ai ? $"IA {Personalities.Defs[ai.Personality].Name.ToLowerInvariant()}, {Worlds.Difficulties[ai.Difficulty].Name.ToLowerInvariant()}" : "Joueur";
        ((Label)c.FindChild("what", true, false)).Text = $"{kind} · réputation {Signed(f.Reputation)} ({Repute(f.Reputation)})";
    });

    /// <summary>An AI's trust in the player, and what moved it lately: the larger first.</summary>
    Part AttitudePart(Side r)
    {
        var why = G.Factions[r].AI!.Why.GetValueOrDefault(Player) ?? [];
        var causes = why.Where(c => Math.Abs(c.Value) >= 0.5).OrderByDescending(c => Math.Abs(c.Value)).Take(6).Select(c => c.Key).ToList();
        return new Part($"attitude:{r}:{string.Join(",", causes)}", () =>
        {
            var col = Column(4);
            col.AddChild(Text("", Palette.Text, 15, bold: true));
            var bar = new ProgressBar { ShowPercentage = false, MaxValue = 1, Step = 0, CustomMinimumSize = new Vector2(0, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
            var fill = UiTheme.Box(Palette.Ok, Palette.Ok, pad: 0);
            fill.SetContentMarginAll(0);
            var bg = UiTheme.Box(Palette.Danger with { A = 0.35f }, Palette.Danger with { A = 0.35f }, pad: 0);
            bg.SetContentMarginAll(0);
            bar.AddThemeStyleboxOverride("fill", fill);
            bar.AddThemeStyleboxOverride("background", bg);
            col.AddChild(bar);
            if (causes.Count > 0) col.AddChild(Text("Ce qui a compté :", Palette.Muted, 13));
            foreach (var _ in causes) col.AddChild(Text("", Palette.Muted, 13));
            col.AddChild(Text("", Palette.Muted, 13));
            return col;
        }, c =>
        {
            var trust = Galaxies.TrustOf(G, r, Player);
            c.GetChild<Label>(0).Text = $"Confiance en toi : {Signed(trust)} sur 100";
            c.GetChild<ProgressBar>(1).Value = Math.Clamp((trust + 100) / 200, 0, 1);
            var i = causes.Count > 0 ? 3 : 2;
            var now = G.Factions[r].AI!.Why.GetValueOrDefault(Player) ?? [];
            foreach (var cause in causes)
            {
                var v = now.GetValueOrDefault(cause);
                var l = c.GetChild<Label>(i++);
                l.Text = $"{Cause(cause)} : {Signed(v)}";
                Paint(l, v >= 0 ? Palette.Ok : Palette.Danger);
            }
            c.GetChild<Label>(i).Text = $"Ta réputation compte aussi : {Signed(G.Factions[Player].Reputation * Galaxies.ReputationWeight)}";
        });
    }

    static string Cause(TrustCause c) => c switch
    {
        TrustCause.Peace => "La paix entre vous",
        TrustCause.Trade => "Le commerce",
        TrustCause.Alliance => "L'alliance",
        TrustCause.JointWar => "La guerre menée ensemble",
        TrustCause.Gifts => "Tes dons et tributs",
        TrustCause.Demands => "Les tributs exigés",
        TrustCause.Attacks => "Les batailles que tu lui as ouvertes",
        TrustCause.Betrayal => "Tes trahisons",
        TrustCause.Witness => "Les trahisons qu'elle t'a vu commettre",
        TrustCause.Help => "L'aide donnée ou refusée",
        _ => "Tes ultimatums",
    };

    /// <summary>The accords in force with `r`, each with how long it holds and how to end it.</summary>
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
        var key = $"accords:{r}:{allied}:{truce is not null}:{trade}:{vision}:{war?.Target}:{tribute?.Payer}:{promise?.Planet}";
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
            if (allied) Line("alliance", "Alliance : vision partagée, aucun combat, victoire commune", () => ConfirmBreak(r, DealKind.Alliance), "Rompre", "Une trahison : la guerre après une courte trêve");
            if (truce is not null) Line("truce", "", () => ConfirmBreak(r, DealKind.Truce), "Rompre", "Vos flottes pourront de nouveau se battre");
            if (trade) Line("trade", $"Commerce : +{N(Galaxies.TradeRate(g, Player, r))}/s chacun", () => End(r, DealKind.Trade), "Arrêter");
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
            if (Galaxies.TruceOf(G, Player, r) is { } t) Set("truce", t.Renewed ? $"Trêve : encore {Left(t.Remaining)}, déjà prolongée" : $"Trêve : encore {Left(t.Remaining)}");
            if (Galaxies.JointWarOf(G, Player, r) is { Target: Side aim } w) Set("war", $"Guerre commune contre {NameOf(aim)} : encore {Left(w.Remaining)}");
            if (Galaxies.TributeOf(G, Player, r) is { } tr)
            {
                Set("tribute", tr.Payer == Player ? $"Tu lui paies {Credits(tr.PerMinute, true)}, encore {Left(tr.Remaining)}"
                    : $"Elle te paie {Credits(tr.PerMinute, true)}, encore {Left(tr.Remaining)}");
            }
            if (G.Promises.Find(p => p.Asker == r && p.Helper == Player) is { } p)
                Set("promise", $"Ta promesse : défendre {PlanetName(p.Planet)}, une flotte là-bas sous {Left(p.Remaining)}");
        });
    }

    /// <summary>An offer or a demand `o` made the player: what it means, its time left, and the answers.</summary>
    Part OfferPart(DealOffer o)
    {
        var (from, kind) = (o.From, o.Kind);
        return new Part($"offer:{from}:{kind}:{o.Target}:{o.Planet}:{o.Terms}:{o.Amount}", () =>
        {
            var p = Panel(10, 8, kind == DealKind.Ultimatum ? Palette.Danger : Palette.Accent);
            var col = Column(6);
            p.AddChild(col);
            col.AddChild(Text(OfferTitle(o), Palette.Text, 15, bold: true, wrap: true));
            col.AddChild(Text(OfferText(o), Palette.Muted, 13, wrap: true));
            var left = Text("", Palette.Muted, 13);
            left.Name = "left";
            col.AddChild(left);
            var row = Row(8);
            row.AddChild(Small(kind == DealKind.Ultimatum ? "Céder" : "Accepter", () => Answer(from, kind, true), "", primary: true));
            row.AddChild(Small(kind == DealKind.Ultimatum ? "Refuser : la guerre" : "Refuser", () => Answer(from, kind, false)));
            col.AddChild(row);
            return p;
        }, c =>
        {
            if (G.Offers.Find(x => x.From == from && x.To == Player && x.Kind == kind) is { } now)
                ((Label)c.FindChild("left", true, false)).Text = kind == DealKind.Ultimatum ? $"Réponds sous {Left(now.Remaining)}, sinon la guerre" : $"Réponse sous {Left(now.Remaining)}";
        });
    }

    // ── Proposals ─────────────────────────────────────────────────────────────────────

    /// <summary>What an AI would answer the player, worked out here on the host, sent by it to a replica.</summary>
    DealPreview? PreviewOf(Side r) => G.Factions[r].AI is { } ai ? ai.Preview ?? GalaxyAi.PreviewFor(G, r, Player) : null;

    /// <summary>The peace price an AI asks of the player, in credits a minute.</summary>
    double PriceOf(Side r) => G.Factions[r].AI is { } ai ? ai.Asked ?? GalaxyAi.PeacePrice(G, r, Player) : 0;

    /// <summary>"Réponse prévue : …", in words and colors; null for a player, who answers for themselves.</summary>
    (string Text, Color Color)? Forecast(Side r, DealWhy? why, double price = 0) => why is not DealWhy w ? null
        : w == DealWhy.Yes ? ("Réponse prévue : elle accepterait", Palette.Ok)
        : ($"Réponse prévue : elle refuserait. {GalaxyAi.Reason(w, price)}", Palette.Danger);

    /// <summary>What the AI would answer the proposal being built.</summary>
    DealWhy? BundleForecast(Side r)
    {
        if (PreviewOf(r) is not { } p) return null;
        var price = PriceOf(r);
        var worth = diploPerMinute ? diploAmount : diploAmount * 60 / Galaxies.TruceTime;
        var tribute = Galaxies.TruceOf(G, Player, r) is not null || !Galaxies.AtWar(G, Player, r) || worth >= price ? DealWhy.Yes : DealWhy.Price;
        return GalaxyAi.BundleWhy(p.Truce, p.Trade, p.Vision, tribute, price, diploTerms, diploAmount, diploPerMinute);
    }

    /// <summary>The plain kind a proposal of one term is, Bundle for several.</summary>
    DealKind ProposalKind => diploTerms switch
    {
        DealTerms.Truce => DealKind.Truce,
        DealTerms.Trade => DealKind.Trade,
        DealTerms.Vision => DealKind.Vision,
        DealTerms.Tribute => DealKind.Tribute,
        _ => DealKind.Bundle,
    };

    /// <summary>
    /// The proposal: terms to tick (a truce, trade, shared vision, a tribute of so much, once or a
    /// minute), the answer the AI would give, and the button that sends it, or why it cannot go.
    /// </summary>
    Part ProposalPart(Side r)
    {
        var g = G;
        var price = PriceOf(r);
        var key = $"proposal:{r}:{diploTerms}:{diploAmount}:{diploPerMinute}:{Standing(Player, r).Word}:{price}";
        return new Part(key, () =>
        {
            var col = Column(8);
            col.AddChild(Text("Coche ce que tu proposes : tout part en une seule offre, acceptée ou refusée d'un bloc.", Palette.Muted, 13, wrap: true));
            var terms = Flow(6);
            void Term(DealTerms t, string label, string tip)
            {
                var b = Small(label, () =>
                {
                    diploTerms ^= t;
                    RefreshDiplomacy();
                }, tip);
                Outline(b, (diploTerms & t) != 0 ? Palette.Accent : null);
                terms.AddChild(b);
            }
            Term(DealTerms.Truce, "Trêve", $"{Minutes(Galaxies.TruceTime)} sans aucun combat entre vous");
            Term(DealTerms.Trade, "Commerce", $"+{N(Galaxies.TradeRate(g, Player, r))}/s chacun tant que dure la paix");
            Term(DealTerms.Vision, "Vision partagée", "Chacun voit ce que voit l'autre, sans alliance");
            Term(DealTerms.Tribute, "Tribut", "Des crédits pour elle, contre la paix ou sa confiance");
            col.AddChild(terms);
            if ((diploTerms & DealTerms.Tribute) != 0)
            {
                var amounts = Flow(6);
                double[] choices = diploPerMinute ? [30, 60, 120, 250] : [200, 500, 1000, 2500];
                var asked = diploPerMinute ? price : Math.Ceiling(price * Galaxies.TruceTime / 60 / 10) * 10;
                foreach (var a in asked > 0 && !choices.Contains(asked) ? [.. choices, asked] : choices)
                {
                    var amount = a;
                    var b = Small(Credits(amount, diploPerMinute), () =>
                    {
                        diploAmount = amount;
                        RefreshDiplomacy();
                    }, amount == asked ? "Le prix qu'elle demande pour la paix" : "");
                    Outline(b, amount == diploAmount ? Palette.Accent : null);
                    amounts.AddChild(b);
                }
                var mode = Small(diploPerMinute ? "par minute" : "en une fois", () =>
                {
                    diploPerMinute = !diploPerMinute;
                    diploAmount = diploPerMinute ? 60 : 500;
                    RefreshDiplomacy();
                }, "Une somme en une fois, ou tant par minute pendant la trêve");
                amounts.AddChild(mode);
                col.AddChild(amounts);
                if (price > 0) col.AddChild(Text($"Elle demande {Credits(price, true)} pour la paix.", Palette.Muted, 13));
            }
            var forecast = Text("", Palette.Muted, 14, wrap: true);
            forecast.Name = "forecast";
            col.AddChild(forecast);
            var send = Small("Envoyer l'offre", () => Propose(r, ProposalKind, diploAmount, diploPerMinute, terms: diploTerms), "", primary: true);
            send.Name = "send";
            send.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            col.AddChild(send);
            return col;
        }, c =>
        {
            var can = diploTerms == DealTerms.None ? Result.Fail("Coche au moins un terme")
                : Galaxies.CanPropose(G, Player, r, ProposalKind, diploAmount, diploPerMinute, terms: diploTerms);
            var send = (Button)c.FindChild("send", true, false);
            send.Disabled = !can.Ok;
            send.TooltipText = can.Ok ? "" : can.Reason;
            var forecast = (Label)c.FindChild("forecast", true, false);
            if (!can.Ok)
            {
                forecast.Text = can.Reason;
                Paint(forecast, Palette.Muted);
            }
            else if (Forecast(r, BundleForecast(r), PriceOf(r)) is var (text, color))
            {
                forecast.Text = text;
                Paint(forecast, color);
            }
            else
            {
                forecast.Text = "Un joueur répond lui-même, dans le temps d'une offre.";
                Paint(forecast, Palette.Muted);
            }
        });
    }

    /// <summary>
    /// What stands apart from the proposal: an alliance, a joint war against each third faction,
    /// a tribute demanded, an ultimatum (credits, or a planet of theirs next to the player's);
    /// each with the answer the AI would give.
    /// </summary>
    Part SpecialPart(Side r)
    {
        var g = G;
        var thirds = g.Factions.Values.Where(t => !t.Eliminated && t.Id != r && t.Id != Player).Select(t => t.Id).ToList();
        var planets = g.Planets.Where(p => Galaxies.CanDemandPlanet(g, Player, r, p.Id).Ok).Select(p => p.Id).ToList();
        var key = $"special:{r}:{Standing(Player, r).Word}:{string.Join(",", thirds)}:{string.Join(",", planets)}";
        return new Part(key, () =>
        {
            var col = Column(8);
            var preview = PreviewOf(r);
            void Act(string label, string tip, DealKind kind, Side? target = null, int? planet = null, double amount = 0, bool perMinute = false, DealWhy? why = null)
            {
                var row = Row(10);
                var can = Galaxies.CanPropose(G, Player, r, kind, amount, perMinute, target, planet);
                var b = Small(label, () => Propose(r, kind, amount, perMinute, target, planet), can.Ok ? tip : can.Reason);
                b.Disabled = !can.Ok;
                row.AddChild(b);
                var (text, color) = !can.Ok ? (can.Reason, Palette.Muted) : Forecast(r, why) ?? ("", Palette.Muted);
                var l = Text(text, color, 13, wrap: true);
                l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(l);
                col.AddChild(row);
            }
            Act("Alliance", "Vision partagée, aucun combat, victoire commune ; la rompre est une trahison", DealKind.Alliance, why: preview?.Alliance);
            foreach (var t in thirds)
            {
                Act($"Guerre commune contre {NameOf(t)}", $"{Minutes(Galaxies.JointWarTime)} de guerre menée ensemble ; faire la paix avec {NameOf(t)} pendant ce temps est une trahison",
                    DealKind.JointWar, t, why: preview?.JointWar.GetValueOrDefault(t, DealWhy.NoThreat));
            }
            var demand = Math.Max(30, Math.Round(Galaxies.IncomeOf(G, r) * 60 * 0.25 / 10) * 10);
            Act($"Exiger {Credits(demand, true)}", "Un tribut contre une trêve : elle paie si elle te craint", DealKind.Demand, amount: demand, perMinute: true,
                why: preview is { } dp ? dp.DemandLimit <= 0 ? DealWhy.NotAfraid : demand <= dp.DemandLimit ? DealWhy.Yes : DealWhy.NoMeans : null);
            var ransom = Math.Max(200, Math.Round(Galaxies.IncomeOf(G, r) * 120 / 10) * 10);
            Act($"Ultimatum : {Credits(ransom, false)}", $"Payer, ou la guerre : elle a {Left(Galaxies.UltimatumTime)} pour répondre. Ta réputation en souffre", DealKind.Ultimatum,
                amount: ransom, why: preview is { } up ? !up.Cedes ? DealWhy.NotAfraid : ransom <= up.YieldLimit ? DealWhy.Yes : DealWhy.NoMeans : null);
            foreach (var p in planets)
            {
                Act($"Ultimatum : {PlanetName(p)}", $"Céder {PlanetName(p)}, ou la guerre. Ta réputation en souffre", DealKind.Ultimatum, planet: p,
                    why: preview is { } cp ? cp.Cedes ? DealWhy.Yes : DealWhy.NotAfraid : null);
            }
            if (planets.Count > 0 && preview is { Cedes: true })
                col.AddChild(Text("Elle ne cède qu'une planète secondaire pour elle, sans grande station.", Palette.Muted, 13, wrap: true));
            return col;
        });
    }

    // ── The chronicle ─────────────────────────────────────────────────────────────────

    /// <summary>The last things that passed between the player and `r`, the newest first.</summary>
    Part HistoryPart(Side r)
    {
        var lines = G.Chronicle.Where(e => (e.A == r && e.B == Player) || (e.A == Player && e.B == r)).TakeLast(14).Reverse().ToList();
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

    /// <summary>A line of the chronicle in words.</summary>
    string Chronicled(DiploEntry e)
    {
        string Who(Side s) => s == Player ? "Toi" : NameOf(s);
        var by = e.By is Side b ? Who(b) : "";
        return e.Kind switch
        {
            ChronicleKind.Signed => e.Deal switch
            {
                DealKind.Truce => "Trêve signée",
                DealKind.Alliance => "Alliance signée",
                DealKind.Trade => "Accord commercial signé",
                DealKind.Vision => "Vision partagée",
                DealKind.JointWar => e.Extra >= 0 ? $"Guerre commune contre {NameOf(new Side(e.Extra))}" : "Guerre commune",
                _ => $"Tribut : {Who(e.A)} paie {Credits(e.Amount, false)}",
            },
            ChronicleKind.Expired => "Fin de la trêve, tenue jusqu'au bout",
            ChronicleKind.Broken => e.Deal switch
            {
                DealKind.Alliance => $"{by} rompt l'alliance",
                DealKind.JointWar => $"{by} trahit la guerre commune",
                _ => $"{by} rompt la trêve",
            },
            ChronicleKind.Ended => e.Deal switch
            {
                DealKind.Trade => "Fin du commerce",
                DealKind.Vision => "Fin de la vision partagée",
                DealKind.JointWar => "Fin de la guerre commune",
                _ => "Fin du tribut",
            },
            ChronicleKind.Refused => $"{by} refuse {Noun(e.Deal)}",
            ChronicleKind.Defaulted => $"{Who(e.A)} cesse de payer le tribut",
            ChronicleKind.Issued => e.Extra >= 0 ? $"Ultimatum de {Who(e.A)} : {PlanetName(e.Extra)}" : $"Ultimatum de {Who(e.A)} : {Credits(e.Amount, false)}",
            ChronicleKind.Accepted => $"{Who(e.B)} cède à l'ultimatum",
            ChronicleKind.Defied => $"{Who(e.B)} défie l'ultimatum",
            ChronicleKind.Ceded => $"{PlanetName(e.Extra)} cédée par {Who(e.A)}",
            ChronicleKind.Asked => e.Extra >= 0 ? $"{Who(e.A)} demande de l'aide pour {PlanetName(e.Extra)}" : $"{Who(e.A)} demande {Credits(e.Amount, false)}",
            ChronicleKind.Kept => $"{Who(e.B)} est venu à l'aide",
            ChronicleKind.Failed => $"{Who(e.B)} n'est pas venu à l'aide",
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
