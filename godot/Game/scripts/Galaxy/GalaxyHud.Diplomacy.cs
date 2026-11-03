// What this version adds to the campaign, as the HUD shows it: the diplomacy list in the drawer
// (truces, alliances, trade and tributes with each rival, an AI's personality and trust), the
// dialogs of an offer made to the player, of a tribute and of a betrayal, the messages of
// diplomacy, random events and ruins, the notes they leave on a planet, and supply.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Campaign;

public partial class GalaxyHud
{
    static readonly Color StormNote = Palette.Hex(0x9b8cff);

    static string Minutes(double seconds) => $"{N(Math.Round(seconds / 60))} min";

    /// <summary>"7 min 05 s", "1 min" on the minute, or "45 s" under a minute.</summary>
    static string Left(double seconds)
    {
        var s = (int)Math.Ceiling(Math.Max(0, seconds));
        return s < 60 ? $"{s} s" : s % 60 == 0 ? $"{s / 60} min" : $"{s / 60} min {s % 60:00} s";
    }

    /// <summary>", hors ravitaillement" for a fleet too far from its planets; nothing otherwise.</summary>
    string Supply(Fleet f) => Galaxies.Supplied(G, f) ? "" : ", hors ravitaillement";

    /// <summary>Every other side but the player's teammates, who need no deal.</summary>
    IEnumerable<Side> Rivals() => G.Factions.Keys.Where(f => !Galaxies.Teammates(G, f, Player));

    /// <summary>The offers a rival made the player, waiting for an answer.</summary>
    List<DealOffer> OffersFrom(Side rival) => G.Offers.FindAll(o => o.From == rival && o.To == Player);

    double Grudge(Side rival) => G.Factions[rival].AI?.Grudges.GetValueOrDefault(Player) ?? 0;

    /// <summary>"60 crédits par minute", "500 crédits".</summary>
    static string Credits(double amount, bool perMinute) => perMinute ? $"{N(Js.Round(amount))} crédits par minute" : $"{N(Js.Round(amount))} crédits";

    /// <summary>A deal as what a refusal turns down: "l'alliance", "le tribut"...</summary>
    static string Noun(DealKind kind) => kind switch
    {
        DealKind.Truce => "la trêve",
        DealKind.Alliance => "l'alliance",
        DealKind.Trade => "l'accord commercial",
        DealKind.Tribute => "le tribut",
        _ => "la demande de tribut",
    };

    /// <summary>What an offer is, as a rival makes it: "Bob propose une alliance".</summary>
    string OfferTitle(Side from, DealKind kind, double amount, bool perMinute) => kind switch
    {
        DealKind.Truce => $"{NameOf(from)} propose une trêve de {Minutes(Galaxies.TruceTime)}",
        DealKind.Alliance => $"{NameOf(from)} propose une alliance",
        DealKind.Trade => $"{NameOf(from)} propose un accord commercial",
        DealKind.Tribute => $"{NameOf(from)} offre un tribut de {Credits(amount, perMinute)}",
        _ => $"{NameOf(from)} exige un tribut de {Credits(amount, perMinute)}",
    };

    /// <summary>What accepting an offer from `from` would mean.</summary>
    string OfferText(Side from, DealKind kind, double amount, bool perMinute) => kind switch
    {
        DealKind.Truce => $"{Minutes(Galaxies.TruceTime)} sans aucun combat entre vous, ni sur les planètes ni en chemin, et aucune flotte envoyée chez l'autre.",
        DealKind.Alliance => "Vous partageriez la vision, ne vous battriez plus et gagneriez ensemble. La rompre serait une trahison.",
        DealKind.Trade => $"Tant que dure la paix, chacun gagnerait {N(Galaxies.TradeRate(G, from, Player))} crédits de plus par seconde.",
        DealKind.Tribute => $"Elle te paierait {Credits(amount, perMinute)} contre une trêve de {Minutes(Galaxies.TruceTime)}.",
        _ => $"Tu lui paierais {Credits(amount, perMinute)} contre une trêve de {Minutes(Galaxies.TruceTime)}. Refuser, c'est rester en guerre.",
    };

    /// <summary>Where the player stands with a rival: peace or war, trade, tribute, and the offers it waits on.</summary>
    string Relation(Side rival)
    {
        var g = G;
        if (g.Factions[rival].Eliminated) return "Éliminée";
        var lines = new List<string>();
        if (Galaxies.Allied(g, Player, rival)) lines.Add("Alliée : vision partagée, aucun combat, victoire commune");
        else if (Galaxies.TruceOf(g, Player, rival) is { } t) lines.Add($"Trêve : encore {Left(t.Remaining)}");
        else lines.Add(Grudge(rival) is var grudge && grudge > 0 ? $"En guerre ; trahie, elle refuse trêve et alliance encore {Left(grudge)}" : "En guerre");
        if (Galaxies.PactOf(g, DealKind.Trade, Player, rival) is not null) lines.Add($"Commerce : +{N(Galaxies.TradeRate(g, Player, rival))}/s chacun");
        if (Galaxies.TributeOf(g, Player, rival) is { } tr)
        {
            lines.Add(tr.Payer == Player ? $"Tu lui paies {Credits(tr.PerMinute, true)}, encore {Left(tr.Remaining)}"
                : $"Elle te paie {Credits(tr.PerMinute, true)}, encore {Left(tr.Remaining)}");
        }
        foreach (var o in g.Offers.Where(o => o.From == Player && o.To == rival)) lines.Add($"Ton offre ({Noun(o.Kind)}) : réponse sous {Left(o.Remaining)}");
        foreach (var o in OffersFrom(rival)) lines.Add($"Son offre ({Noun(o.Kind)}) : réponse sous {Left(o.Remaining)}");
        return string.Join("\n", lines);
    }

    /// <summary>An AI rival's personality, and how far it trusts the player.</summary>
    string Mood(Side rival) => G.Factions[rival].AI is { } ai
        ? $"{Personalities.Defs[ai.Personality].Name} · confiance {N(Js.Round(Galaxies.TrustOf(G, rival, Player)))}" : "";

    /// <summary>What the row of a rival shows, for it to be rebuilt when that changes: peace, trade, tribute, offers.</summary>
    string StateOf(Side r)
    {
        var g = G;
        if (g.Factions[r].Eliminated) return "out";
        var peace = Galaxies.Allied(g, Player, r) ? "ally" : Galaxies.AtPeace(g, Player, r) ? "truce" : "war";
        var trade = Galaxies.PactOf(g, DealKind.Trade, Player, r) is not null ? ":trade" : "";
        var tribute = Galaxies.TributeOf(g, Player, r) is { } t ? t.Payer == Player ? ":pays" : ":paid" : "";
        return $"{peace}{trade}{tribute}:{string.Join(",", OffersFrom(r).Select(o => o.Kind))}";
    }

    /// <summary>The diplomacy list of the drawer: each rival with where things stand and what can be done.</summary>
    List<Part> DiplomacyParts()
    {
        var g = G;
        var parts = new List<Part>
        {
            HudKit.TextPart($"Trêve : {Minutes(Galaxies.TruceTime)} sans combat. Alliance : vision partagée, aucun combat, victoire commune ; la rompre est une trahison. "
                + "Commerce : des crédits pour les deux tant que dure la paix, plus encore entre planètes voisines. Tribut : des crédits contre une trêve, une fois ou par minute. "
                + "Une IA répond selon sa force, sa personnalité et sa confiance en toi ; elle n'oublie pas une trahison. Jamais d'accord avec les Errants.", Palette.Muted, 13),
        };
        foreach (var r in Rivals())
        {
            parts.Add(new Part($"rival:{r}:{StateOf(r)}", () => RivalRow(r), c =>
            {
                ((Label)c.FindChild("Relation", true, false)).Text = Relation(r);
                if (c.FindChild("mood", true, false) is Label mood) mood.Text = Mood(r);
            }));
        }
        string Pair(Side a, Side b) => $"{NameOf(a)} et {NameOf(b)}";
        var truces = g.Truces.Where(t => t.A != Player && t.B != Player).Select(t => $"{Pair(t.A, t.B)} ({Left(t.Remaining)})").ToList();
        if (truces.Count > 0) parts.Add(HudKit.TextPart("Trêves entre les autres : " + string.Join(", ", truces), Palette.Muted, 13));
        var alliances = g.Pacts.Where(p => p.Kind == DealKind.Alliance && p.A != Player && p.B != Player).Select(p => Pair(p.A, p.B)).ToList();
        if (alliances.Count > 0) parts.Add(HudKit.TextPart("Alliances entre les autres : " + string.Join(", ", alliances), Palette.Muted, 13));
        return parts;
    }

    static Button Small(string text, Action act, string tip = "", bool primary = false)
    {
        var b = Btn(text, act, primary);
        b.CustomMinimumSize = new Vector2(0, 32);
        b.TooltipText = tip;
        return b;
    }

    /// <summary>A rival in the diplomacy list: emblem, name, mood, relation, the offers it made, and the buttons its state allows.</summary>
    Control RivalRow(Side r)
    {
        var g = G;
        var col = HudKit.Column(4);
        var head = HudKit.Row(8);
        head.AddChild(Emblem(r));
        var name = HudKit.Column(0);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.AddChild(Text(NameOf(r), bold: true));
        if (g.Factions[r].AI is not null)
        {
            var mood = Text("", Palette.Accent, 13);
            mood.Name = "mood";
            name.AddChild(mood);
        }
        var relation = Text("", Palette.Muted, 13, wrap: true);
        relation.Name = "Relation";
        name.AddChild(relation);
        head.AddChild(name);
        col.AddChild(head);
        if (g.Factions[r].Eliminated) return col;
        foreach (var o in OffersFrom(r))
        {
            var (kind, amount, perMinute) = (o.Kind, o.Amount, o.PerMinute);
            col.AddChild(Text(OfferTitle(r, kind, amount, perMinute), Palette.Accent, 13, wrap: true));
            var answer = HudKit.Row(6);
            answer.AddChild(Small("Accepter", () => Answer(r, kind, true), OfferText(r, kind, amount, perMinute), primary: true));
            answer.AddChild(Small("Refuser", () => Answer(r, kind, false)));
            col.AddChild(answer);
        }
        var buttons = HudKit.Flow(6);
        void Add(string text, Action act, string tip) => buttons.AddChild(Small(text, act, tip));
        var allied = Galaxies.Allied(g, Player, r);
        var truce = Galaxies.AtPeace(g, Player, r);
        var trade = Galaxies.PactOf(g, DealKind.Trade, Player, r) is not null;
        var tribute = Galaxies.TributeOf(g, Player, r);
        if (!allied && !truce) Add("Trêve", () => Propose(r, DealKind.Truce), $"Proposer {Minutes(Galaxies.TruceTime)} sans combat");
        if (!allied) Add("Alliance", () => Propose(r, DealKind.Alliance), "Proposer une alliance : vision partagée, aucun combat, victoire commune");
        if ((allied || truce) && !trade)
        {
            Add("Commerce", () => Propose(r, DealKind.Trade), $"Proposer un accord commercial : +{N(Galaxies.TradeRate(g, Player, r))}/s chacun tant que dure la paix");
        }
        if (!allied && tribute is null)
        {
            Add("Payer un tribut", () => TributeDialog(r, DealKind.Tribute), "Offrir des crédits contre une trêve");
            Add("Exiger un tribut", () => TributeDialog(r, DealKind.Demand), "Exiger des crédits contre une trêve");
        }
        if (trade) Add("Arrêter le commerce", () => End(r, DealKind.Trade), "Mettre fin à l'accord commercial");
        if (tribute?.Payer == Player) Add("Cesser de payer", () => ConfirmBreak(r, DealKind.Tribute), "Rompt la trêve que le tribut achète");
        if (tribute?.Payee == Player) Add("Lever le tribut", () => End(r, DealKind.Tribute), "Elle cesse de te payer ; la trêve tient");
        if (truce) Add("Rompre la trêve", () => ConfirmBreak(r, DealKind.Truce), "Vos flottes pourront de nouveau se battre");
        if (allied) Add("Rompre l'alliance", () => ConfirmBreak(r, DealKind.Alliance), "Une trahison : la guerre après une courte trêve");
        if (buttons.GetChildCount() > 0) col.AddChild(buttons);
        return col;
    }

    void Propose(Side rival, DealKind kind, double amount = 0, bool perMinute = false)
    {
        var can = Galaxies.CanPropose(G, Player, rival, kind, amount, perMinute);
        if (!can.Ok)
        {
            Notify(can.Reason);
            return;
        }
        var r = scene.Sink.Send(new GalaxyCommand.ProposeDeal(rival, kind, amount, perMinute));
        // A deal signed says so itself (TruceSigned, PactSigned, TributeSigned), an AI's refusal too (DealRefused).
        // Over the network, another player answers in their own time.
        if (r.Ok && G.Factions[rival].AI is null) Notify($"Offre faite à {NameOf(rival)} : réponse sous {Left(Galaxies.OfferTime)}");
        RefreshSoon();
    }

    void Answer(Side rival, DealKind kind, bool yes)
    {
        var r = scene.Sink.Send(new GalaxyCommand.AnswerDeal(rival, kind, yes));
        if (yes && !r.Ok) Notify($"L'offre de {NameOf(rival)} n'est plus valable");
        else if (!yes) Notify($"Offre de {NameOf(rival)} refusée");
        RefreshSoon();
    }

    void End(Side rival, DealKind kind)
    {
        scene.Sink.Send(new GalaxyCommand.EndDeal(rival, kind));
        RefreshSoon();
    }

    /// <summary>Breaking a truce or an alliance, or no longer paying for peace, asks first: each is a betrayal.</summary>
    void ConfirmBreak(Side rival, DealKind kind)
    {
        var (p, box) = Dialog();
        var who = NameOf(rival);
        var (title, text, act) = kind switch
        {
            DealKind.Alliance => ($"Rompre l'alliance avec {who} ?",
                $"Vous ne partagerez plus la vision. {Minutes(Galaxies.NoticeTime)} de trêve pour retirer vos flottes, puis la guerre. {who} refusera toute trêve "
                + $"et toute alliance pendant {Minutes(2 * Galaxies.GrudgeTime)}, et les autres factions te feront moins confiance.", "Rompre"),
            DealKind.Tribute => ($"Cesser de payer {who} ?",
                $"C'est rompre la trêve que le tribut achète : vos flottes pourront de nouveau se battre, et {who} refusera toute trêve pendant {Minutes(Galaxies.GrudgeTime)}.",
                "Cesser de payer"),
            _ => ($"Rompre la trêve avec {who} ?",
                $"Vos flottes pourront de nouveau se battre, sur les planètes comme en chemin. {who} refusera alors toute trêve pendant {Minutes(Galaxies.GrudgeTime)}.", "Rompre"),
        };
        box.AddChild(Heading(title, rival, 22));
        box.AddChild(HudKit.Para(text));
        box.AddChild(Actions(Btn("Annuler", CloseModal), Btn(act, () =>
        {
            CloseModal();
            End(rival, kind);
        }, primary: true)));
        OpenModal(p);
    }

    /// <summary>Credits once, and by the minute, a tribute may be.</summary>
    static readonly double[] Once = [200, 500, 1000], ByMinute = [30, 60, 120];

    /// <summary>A tribute offered or demanded: how much, once or by the minute.</summary>
    void TributeDialog(Side rival, DealKind kind)
    {
        var (p, box) = Dialog();
        var who = NameOf(rival);
        var offer = kind == DealKind.Tribute;
        box.AddChild(Heading(offer ? $"Payer un tribut à {who}" : $"Exiger un tribut de {who}", rival, 22));
        box.AddChild(HudKit.Para(offer
            ? $"Des crédits contre une trêve de {Minutes(Galaxies.TruceTime)}. Plus une faction se sent forte, plus elle demande ; par minute, tu paies tant que dure la trêve, et cesser de payer la rompt."
            : $"Elle paie si elle te craint et en a les moyens, contre une trêve de {Minutes(Galaxies.TruceTime)}. Elle te le reprochera."));
        var choices = HudKit.Flow(6);
        void Choice(double amount, bool perMinute)
        {
            var b = Small(Credits(amount, perMinute), () =>
            {
                CloseModal();
                Propose(rival, kind, amount, perMinute);
            });
            b.Disabled = offer && !perMinute && G.Factions[Player].Credits < amount;
            choices.AddChild(b);
        }
        foreach (var a in Once) Choice(a, false);
        foreach (var a in ByMinute) Choice(a, true);
        box.AddChild(choices);
        box.AddChild(Actions(Btn("Annuler", CloseModal)));
        OpenModal(p);
    }

    /// <summary>A rival offers the player a deal: answer now, or later from the diplomacy list.</summary>
    void ShowOffer(DealOffered o)
    {
        var (p, box) = Dialog();
        box.AddChild(Heading(OfferTitle(o.From, o.Kind, o.Amount, o.PerMinute), o.From, 22));
        box.AddChild(HudKit.Para(OfferText(o.From, o.Kind, o.Amount, o.PerMinute)));
        box.AddChild(HudKit.Para($"L'offre tient {Left(Galaxies.OfferTime)} ; la liste Diplomatie (T) la garde jusque-là.", Palette.Muted));
        box.AddChild(Actions(
            Btn("Accepter", () =>
            {
                CloseModal();
                Answer(o.From, o.Kind, true);
            }, primary: true),
            Btn("Refuser", () =>
            {
                CloseModal();
                Answer(o.From, o.Kind, false);
            }),
            Btn("Plus tard", CloseModal)));
        OpenModal(p);
    }

    string PlanetName(int id) => G.Planets[id].Name;

    string Holder(Side? f) => f is Side o ? NameOf(o) : "neutre";

    /// <summary>Messages for truces, random events and ruins; the rest is OnEvent's.</summary>
    void OnDepthEvent(GalaxyEvent ev)
    {
        var player = Player;
        switch (ev)
        {
            case DealOffered o when o.To == player:
                Sfx.Play(Sound.Alert);
                Notify(OfferTitle(o.From, o.Kind, o.Amount, o.PerMinute));
                if (!DialogOpen) ShowOffer(o);
                break;
            case DealRefused r when r.From == player:
                Notify(r.Reason.Length > 0 ? $"{NameOf(r.To)} refuse {Noun(r.Kind)}. {r.Reason}." : $"{NameOf(r.To)} refuse {Noun(r.Kind)}");
                break;
            case PactSigned s when s.A == player || s.B == player:
            {
                var partner = s.A == player ? s.B : s.A;
                Notify(s.Kind == DealKind.Alliance ? $"Alliance signée avec {NameOf(partner)} : vision partagée, aucun combat, victoire commune"
                    : $"Accord commercial avec {NameOf(partner)} : +{N(Galaxies.TradeRate(G, s.A, s.B))}/s chacun");
                Sfx.Play(Sound.Built);
                break;
            }
            case PactSigned s:
                Notify(s.Kind == DealKind.Alliance ? $"Alliance entre {NameOf(s.A)} et {NameOf(s.B)}" : $"Accord commercial entre {NameOf(s.A)} et {NameOf(s.B)}");
                break;
            case PactEnded e when e.A == player || e.B == player:
            {
                var who = NameOf(e.A == player ? e.B : e.A);
                Notify(e.Kind switch
                {
                    DealKind.Alliance when e.By == player => $"Alliance rompue avec {who} : {Minutes(Galaxies.NoticeTime)} de trêve pour retirer vos flottes",
                    DealKind.Alliance => $"{who} rompt votre alliance : {Minutes(Galaxies.NoticeTime)} de trêve, puis la guerre",
                    _ when e.By == player => $"Commerce arrêté avec {who}",
                    _ when e.By is null => $"Fin du commerce avec {who} : la paix est rompue",
                    _ => $"{who} arrête le commerce",
                });
                Sfx.Play(Sound.Alert);
                break;
            }
            case PactEnded e:
                Notify(e.Kind == DealKind.Alliance ? $"Fin de l'alliance entre {NameOf(e.A)} et {NameOf(e.B)}" : $"Fin de l'accord commercial entre {NameOf(e.A)} et {NameOf(e.B)}");
                break;
            case TributeSigned tb when tb.Payer == player:
                Notify($"Tribut à {NameOf(tb.Payee)} : {Credits(tb.Amount, tb.PerMinute)}, contre la paix");
                break;
            case TributeSigned tb when tb.Payee == player:
                Notify($"Tribut de {NameOf(tb.Payer)} : {Credits(tb.Amount, tb.PerMinute)}, contre la paix");
                Sfx.Play(Sound.Built);
                break;
            case TributeSigned tb:
                Notify($"{NameOf(tb.Payer)} paie un tribut à {NameOf(tb.Payee)}");
                break;
            case TributeEnded tb when tb.Payer == player || tb.Payee == player:
            {
                var who = NameOf(tb.Payer == player ? tb.Payee : tb.Payer);
                Notify(tb.By is null ? $"Fin du tribut avec {who}"
                    : tb.By == tb.Payee ? tb.Payee == player ? $"Tribut de {who} levé" : $"{who} lève le tribut"
                    : tb.Payer == player ? $"Tribut à {who} cessé : la trêve est rompue" : $"{who} cesse de payer : la trêve est rompue");
                break;
            }
            case TruceSigned s:
                Notify(s.A == player || s.B == player
                    ? $"Trêve signée avec {NameOf(s.A == player ? s.B : s.A)} : {Minutes(Galaxies.TruceTime)} sans combat"
                    : $"Trêve entre {NameOf(s.A)} et {NameOf(s.B)}");
                break;
            case TruceEnded e when e.A == player || e.B == player:
                var other = e.A == player ? e.B : e.A;
                Notify(e.BrokenBy == player
                    ? $"Trêve rompue avec {NameOf(other)} : plus de trêve avec elle avant {Minutes(Galaxies.GrudgeTime)}"
                    : $"Fin de la trêve avec {NameOf(other)}");
                Sfx.Play(Sound.Alert);
                break;
            case TruceEnded e:
                Notify($"Fin de la trêve entre {NameOf(e.A)} et {NameOf(e.B)}");
                break;
            case IncidentStarted i:
                OnIncident(i);
                break;
            case IncidentEnded i:
                OnIncidentEnd(i);
                break;
            case RuinsExplored r when r.Side == player:
                var tech = r.Tech is TechId t ? $", {Techs.Defs[t].Name} niveau {G.Factions[player].Research[t]}" : "";
                Notify($"Ruines de {PlanetName(r.PlanetId)} explorées : +{N(r.Credits)} crédits{tech}", r.PlanetId);
                Sfx.Play(Sound.Built);
                break;
            case RuinsExplored r:
                Notify($"{NameOf(r.Side)} explore les ruines de {PlanetName(r.PlanetId)}", r.PlanetId);
                break;
        }
    }

    void OnIncident(IncidentStarted i)
    {
        switch (i.Kind)
        {
            case IncidentKind.Derelict:
                Notify($"Une flotte abandonnée dérive à {PlanetName(i.Planet)} : à la première flotte qui s'y arrête", i.Planet);
                break;
            case IncidentKind.WarBand when i.Side == Player:
                Notify($"Une bande errante se rassemble à {PlanetName(i.Planet)} : elle frappera {PlanetName(i.Other)} dans {N(Galaxies.WarBandWarning)} s", i.Other);
                Sfx.Play(Sound.Alert);
                break;
            case IncidentKind.WarBand:
                Notify($"Une bande errante menace {PlanetName(i.Other)} ({Holder(i.Side)})", i.Other);
                break;
            case IncidentKind.IonStorm:
                Notify($"Tempête ionique entre {PlanetName(i.Planet)} et {PlanetName(i.Other)} : vitesse divisée par deux pendant "
                    + Minutes(Galaxies.StormTime), i.Planet);
                break;
            case IncidentKind.TradeBoom:
                Notify($"Essor commercial à {PlanetName(i.Planet)}{(i.Side == Player ? "" : $" ({Holder(i.Side)})")} : revenu doublé pendant "
                    + Minutes(Galaxies.BoomTime), i.Planet);
                break;
        }
    }

    void OnIncidentEnd(IncidentEnded i)
    {
        var mine = i.Side == Player;
        switch (i.Kind)
        {
            case IncidentKind.Derelict when i.Side is Side by:
                Notify(mine ? $"Flotte abandonnée récupérée à {PlanetName(i.Planet)}" : $"{NameOf(by)} récupère la flotte abandonnée de {PlanetName(i.Planet)}", i.Planet);
                if (mine) Sfx.Play(Sound.Built);
                break;
            case IncidentKind.Derelict:
                Notify($"La flotte abandonnée de {PlanetName(i.Planet)} s'éloigne", i.Planet);
                break;
            case IncidentKind.WarBand:
                Notify(mine ? $"{PlanetName(i.Planet)} pillée par une bande errante : ses mines sont perdues"
                    : $"Une bande errante pille {PlanetName(i.Planet)} ({Holder(i.Side)})", i.Planet);
                if (mine) Sfx.Play(Sound.Alert);
                break;
            case IncidentKind.IonStorm:
                Notify($"La tempête ionique entre {PlanetName(i.Planet)} et {PlanetName(i.Other)} se dissipe", i.Planet);
                break;
            case IncidentKind.TradeBoom when G.Planets[i.Planet].Owner == Player:
                Notify($"L'essor commercial de {PlanetName(i.Planet)} retombe", i.Planet);
                break;
        }
    }

    /// <summary>What goes on at a planet beyond its special: random events there, a truce with its owner.</summary>
    List<(string Title, string Text, Color Color)> DepthNotes(Planet p)
    {
        var g = G;
        var notes = new List<(string, string, Color)>();
        foreach (var i in g.Ongoing)
        {
            switch (i.Kind)
            {
                case IncidentKind.Derelict when i.Planet == p.Id:
                    notes.Add(("Flotte abandonnée", $"{DescribeShips(i.Ships)}, abîmés : à la première flotte qui s'arrête ici.", Palette.Accent));
                    break;
                case IncidentKind.WarBand when i.Planet == p.Id:
                    notes.Add(("Bande errante", $"Elle se rassemble ici et frappera {PlanetName(i.Other)}. Elle pille, ne prend rien.", Palette.Of(Faction.Errant)));
                    break;
                case IncidentKind.WarBand when i.Other == p.Id:
                    notes.Add(("Bande errante en approche", $"Elle se rassemble à {PlanetName(i.Planet)} : sans défense, la planète perdra ses mines.", Palette.Danger));
                    break;
                case IncidentKind.IonStorm when i.Planet == p.Id || i.Other == p.Id:
                    notes.Add(("Tempête ionique", $"Vers {PlanetName(i.Planet == p.Id ? i.Other : i.Planet)} : vitesse divisée par deux.", StormNote));
                    break;
                case IncidentKind.TradeBoom when i.Planet == p.Id:
                    notes.Add(("Essor commercial", "Revenu de la planète doublé.", Palette.Accent));
                    break;
            }
        }
        if (p.Owner is Side o && o != Player && Galaxies.AtPeace(g, Player, o))
        {
            notes.Add(("Trêve", $"Avec {NameOf(o)} : ni combat ici, ni flotte envoyée ; tes flottes y passent.", Palette.Ok));
        }
        if (p.Owner is Side ally && ally != Player && Galaxies.Allied(g, Player, ally))
        {
            notes.Add(("Alliée", $"Avec {NameOf(ally)} : vision partagée, aucun combat ; tes flottes y vont.", Palette.Ok));
        }
        return notes;
    }

    /// <summary>What the planet panel shows of DepthNotes and specials, so that it is rebuilt when they change.</summary>
    string DepthSignature(Planet p) => $"{p.Special}:{string.Join(",", DepthNotes(p).Select(n => n.Title))}";
}
