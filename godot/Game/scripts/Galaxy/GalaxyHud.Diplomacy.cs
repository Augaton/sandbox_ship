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

    /// <summary>", hors ravitaillement" for a fleet too far from its stations; nothing otherwise.</summary>
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
        DealKind.Vision => "la vision partagée",
        DealKind.JointWar => "la guerre commune",
        DealKind.Ultimatum => "l'ultimatum",
        DealKind.Help => "la demande d'aide",
        DealKind.Bundle => "l'accord",
        _ => "la demande de tribut",
    };

    /// <summary>What an offer is, as a rival makes it: "Bob propose une alliance".</summary>
    string OfferTitle(DealOffer o) => OfferTitle(o.From, o.Kind, o.Amount, o.PerMinute, o.Target, o.Planet, o.Terms);

    string OfferTitle(DealOffered o) => OfferTitle(o.From, o.Kind, o.Amount, o.PerMinute, o.Target, o.Planet, o.Terms);

    string OfferTitle(Side from, DealKind kind, double amount, bool perMinute, Side? target = null, int? planet = null, DealTerms terms = DealTerms.None) => kind switch
    {
        DealKind.Truce => $"{NameOf(from)} propose une trêve de {Minutes(Galaxies.TruceTime)}",
        DealKind.Alliance => $"{NameOf(from)} propose une alliance",
        DealKind.Trade => $"{NameOf(from)} propose un accord commercial",
        DealKind.Vision => $"{NameOf(from)} propose de partager la vision",
        DealKind.Tribute => $"{NameOf(from)} offre un tribut de {Credits(amount, perMinute)}",
        DealKind.JointWar => $"{NameOf(from)} propose une guerre commune contre {(target is Side t ? NameOf(t) : "?")}",
        DealKind.Ultimatum => planet is int p ? $"Ultimatum de {NameOf(from)} : cède {PlanetName(p)}" : $"Ultimatum de {NameOf(from)} : {Credits(amount, false)}",
        DealKind.Help => planet is int h ? $"{NameOf(from)} demande de l'aide pour {PlanetName(h)}" : $"{NameOf(from)} demande {Credits(amount, false)}",
        DealKind.Bundle => $"{NameOf(from)} propose un accord : {Terms(terms, amount, perMinute)}",
        _ => $"{NameOf(from)} exige un tribut de {Credits(amount, perMinute)}",
    };

    /// <summary>A bundle's terms in words: "trêve, commerce, tribut de 60 crédits par minute".</summary>
    static string Terms(DealTerms terms, double amount, bool perMinute)
    {
        var bits = new List<string>();
        if ((terms & DealTerms.Truce) != 0) bits.Add("trêve");
        if ((terms & DealTerms.Trade) != 0) bits.Add("commerce");
        if ((terms & DealTerms.Vision) != 0) bits.Add("vision partagée");
        if ((terms & DealTerms.Tribute) != 0) bits.Add($"tribut de {Credits(amount, perMinute)}");
        return string.Join(", ", bits);
    }

    /// <summary>What accepting an offer would mean.</summary>
    string OfferText(DealOffer o) => OfferText(o.From, o.Kind, o.Amount, o.PerMinute, o.Target, o.Planet);

    string OfferText(DealOffered o) => OfferText(o.From, o.Kind, o.Amount, o.PerMinute, o.Target, o.Planet);

    string OfferText(Side from, DealKind kind, double amount, bool perMinute, Side? target = null, int? planet = null) => kind switch
    {
        DealKind.Truce => $"{Minutes(Galaxies.TruceTime)} sans aucun combat entre vous, ni sur les planètes ni en chemin, et aucune flotte envoyée chez l'autre.",
        DealKind.Alliance => "Vous partageriez la vision, ne vous battriez plus et gagneriez ensemble. La rompre serait une trahison.",
        DealKind.Trade => $"Tant que dure la paix, chacun gagnerait {N(Galaxies.TradeRate(G, from, Player))} crédits de plus par seconde.",
        DealKind.Vision => "Chacun verrait ce que voit l'autre, sans alliance. La guerre y mettrait fin.",
        DealKind.Tribute => $"Elle te paierait {Credits(amount, perMinute)} contre une trêve de {Minutes(Galaxies.TruceTime)}.",
        DealKind.JointWar => $"{Minutes(Galaxies.JointWarTime)} de guerre menée ensemble contre {(target is Side t ? NameOf(t) : "?")}. Faire la paix avec elle pendant ce temps serait une trahison.",
        DealKind.Ultimatum => planet is int p ? $"Céder {PlanetName(p)} avec ses installations, ou la guerre. Refuser, c'est la guerre."
            : $"Lui payer {Credits(amount, false)}, ou la guerre. Refuser, c'est la guerre.",
        DealKind.Help => planet is int h ? $"Promettre d'envoyer une flotte à {PlanetName(h)} sous {Left(Galaxies.HelpTime)}. Tenir sa promesse renforce sa confiance ; ne pas venir la ruine."
            : $"Lui donner {Credits(amount, false)}. Refuser la décevra.",
        DealKind.Bundle => "Tous ces termes ensemble, à accepter ou refuser d'un bloc.",
        _ => $"Tu lui paierais {Credits(amount, perMinute)} contre une trêve de {Minutes(Galaxies.TruceTime)}. Refuser, c'est rester en guerre.",
    };

    void Propose(Side rival, DealKind kind, double amount = 0, bool perMinute = false, Side? target = null, int? planet = null, DealTerms terms = DealTerms.None)
    {
        var can = Galaxies.CanPropose(G, Player, rival, kind, amount, perMinute, target, planet, terms);
        if (!can.Ok)
        {
            Notify(can.Reason);
            return;
        }
        var r = scene.Sink.Send(new GalaxyCommand.ProposeAccord(rival, kind, amount, perMinute, target, planet, terms));
        // A deal signed says so itself (TruceSigned, PactSigned, TributeSigned), an AI's refusal too (DealRefused).
        // Over the network, another player answers in their own time.
        if (r.Ok && G.Factions[rival].AI is null) Notify($"Offre faite à {NameOf(rival)} : réponse sous {Left(kind == DealKind.Ultimatum ? Galaxies.UltimatumTime : Galaxies.OfferTime)}");
        if (r.Ok) diploTerms = DealTerms.None;
        RefreshSoon();
    }

    static Button Small(string text, Action act, string tip = "", bool primary = false)
    {
        var b = Btn(text, act, primary);
        b.CustomMinimumSize = new Vector2(0, 32);
        b.TooltipText = tip;
        return b;
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

    /// <summary>A rival offers the player a deal: answer now, or later from the diplomacy screen. An ultimatum waits for its answer.</summary>
    void ShowOffer(DealOffered o)
    {
        var (p, box) = Dialog();
        var ultimatum = o.Kind == DealKind.Ultimatum;
        box.AddChild(Heading(OfferTitle(o), o.From, 22));
        box.AddChild(HudKit.Para(OfferText(o)));
        box.AddChild(HudKit.Para(ultimatum ? $"Tu as {Left(Galaxies.UltimatumTime)} pour répondre."
            : $"L'offre tient {Left(Galaxies.OfferTime)} ; l'écran Diplomatie (T) la garde jusque-là.", Palette.Muted));
        box.AddChild(Actions(
            Btn(ultimatum ? "Céder" : "Accepter", () =>
            {
                CloseModal();
                Answer(o.From, o.Kind, true);
            }, primary: true),
            Btn(ultimatum ? "Refuser : la guerre" : "Refuser", () =>
            {
                CloseModal();
                Answer(o.From, o.Kind, false);
            }),
            Btn("Diplomatie", () =>
            {
                CloseModal();
                OpenDiplomacy(o.From);
            })));
        OpenModal(p, stays: ultimatum);
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
            case DealRefused r when r.From == player && r.Kind == DealKind.Ultimatum:
                Notify(r.Reason.Length > 0 ? $"{NameOf(r.To)} défie ton ultimatum. {r.Reason}." : $"{NameOf(r.To)} défie ton ultimatum");
                break;
            case DealRefused r when r.From == player:
                Notify(r.Reason.Length > 0 ? $"{NameOf(r.To)} refuse {Noun(r.Kind)}. {r.Reason}." : $"{NameOf(r.To)} refuse {Noun(r.Kind)}");
                break;
            case UltimatumEnded u when u.To == player:
                Notify(u.Accepted ? $"Tu as cédé à l'ultimatum de {NameOf(u.From)}" : $"Ultimatum de {NameOf(u.From)} rejeté : c'est la guerre");
                if (!u.Accepted) Sfx.Play(Sound.Alert);
                break;
            case UltimatumEnded u when u.From == player:
                Notify(u.Accepted ? $"{NameOf(u.To)} cède à ton ultimatum" : $"{NameOf(u.To)} rejette ton ultimatum : c'est la guerre");
                break;
            case PlanetCeded c:
                Notify(c.To == player ? $"{PlanetName(c.PlanetId)} t'est cédée par {NameOf(c.From)}"
                    : c.From == player ? $"{PlanetName(c.PlanetId)} cédée à {NameOf(c.To)}" : $"{NameOf(c.From)} cède {PlanetName(c.PlanetId)} à {NameOf(c.To)}", c.PlanetId);
                break;
            case HelpAnswered h when h.Helper == player:
                Notify(h.PlanetId < 0 ? $"Aide envoyée à {NameOf(h.Asker)}" : h.Kept ? $"Promesse tenue : {NameOf(h.Asker)} t'en sait gré" : $"Promesse non tenue : {NameOf(h.Asker)} t'en veut");
                break;
            case AiSaid s when s.To == player:
            {
                // Who speaks, and its words under it, each translated whole. A warning, a threat or a
                // call for help is worth a look: the button opens the screen on who said it.
                var (who, words) = (NameOf(s.From), RemarkText(s.From, s.What));
                var urgent = s.What is Remark.Warning or Remark.Threat or Remark.AskHelp or Remark.Coalition;
                toasts.Add(who, urgent ? () => OpenDiplomacy(s.From) : null, Palette.Accent, [words], life: 6000);
                Log(who, [words], null);
                break;
            }
            case PactSigned s when s.A == player || s.B == player:
            {
                var partner = s.A == player ? s.B : s.A;
                Notify(s.Kind switch
                {
                    DealKind.Alliance => $"Alliance signée avec {NameOf(partner)} : vision partagée, aucun combat, victoire commune",
                    DealKind.Vision => $"Vision partagée avec {NameOf(partner)}",
                    DealKind.JointWar => $"Guerre commune avec {NameOf(partner)} contre {(s.Target is Side aim ? NameOf(aim) : "?")}",
                    _ => $"Accord commercial avec {NameOf(partner)} : +{N(Galaxies.TradeRate(G, s.A, s.B))}/s chacun",
                });
                Sfx.Play(Sound.Built);
                break;
            }
            case PactSigned s when s.Kind == DealKind.JointWar && s.Target == player:
                Notify($"{NameOf(s.A)} et {NameOf(s.B)} se liguent contre toi !");
                Sfx.Play(Sound.Alert);
                break;
            case PactSigned s:
                Notify(s.Kind switch
                {
                    DealKind.Alliance => $"Alliance entre {NameOf(s.A)} et {NameOf(s.B)}",
                    DealKind.JointWar => $"{NameOf(s.A)} et {NameOf(s.B)} font la guerre ensemble à {(s.Target is Side aim ? NameOf(aim) : "?")}",
                    _ => $"Accord entre {NameOf(s.A)} et {NameOf(s.B)}",
                });
                break;
            case PactEnded e when e.A == player || e.B == player:
            {
                var who = NameOf(e.A == player ? e.B : e.A);
                Notify(e.Kind switch
                {
                    DealKind.Alliance when e.By == player => $"Alliance rompue avec {who} : {Minutes(Galaxies.NoticeTime)} de trêve pour retirer vos flottes",
                    DealKind.Alliance => $"{who} rompt votre alliance : {Minutes(Galaxies.NoticeTime)} de trêve, puis la guerre",
                    DealKind.Vision when e.By == player => $"Tu cesses de partager la vision avec {who}",
                    DealKind.Vision when e.By is null => $"Fin de la vision partagée avec {who}",
                    DealKind.Vision => $"{who} cesse de partager la vision",
                    DealKind.JointWar when e.By is null => $"Fin de la guerre commune avec {who}",
                    DealKind.JointWar when e.By == player => $"Tu quittes la guerre commune avec {who}",
                    DealKind.JointWar => $"{who} trahit votre guerre commune",
                    _ when e.By == player => $"Commerce arrêté avec {who}",
                    _ when e.By is null => $"Fin du commerce avec {who} : la paix est rompue",
                    _ => $"{who} arrête le commerce",
                });
                Sfx.Play(Sound.Alert);
                break;
            }
            case PactEnded e when e.Kind is DealKind.Alliance or DealKind.JointWar:
                Notify(e.Kind == DealKind.Alliance ? $"Fin de l'alliance entre {NameOf(e.A)} et {NameOf(e.B)}" : $"Fin de la guerre commune entre {NameOf(e.A)} et {NameOf(e.B)}");
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
                    : e.BrokenBy is not null ? $"{NameOf(other)} rompt la trêve !"
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
                Alert(G.Planets[i.Planet].Owner is null
                    ? $"Une bande errante se rassemble à {PlanetName(i.Planet)} : elle frappera {PlanetName(i.Other)} dans {N(Galaxies.WarBandWarning)} s"
                    : $"Une bande errante sort de l'ombre près de {PlanetName(i.Planet)} : elle frappera {PlanetName(i.Other)} dans {N(Galaxies.WarBandWarning)} s", i.Other);
                break;
            case IncidentKind.WarBand:
                Notify($"Une bande errante menace {PlanetName(i.Other)} ({Holder(i.Side)})", i.Other);
                break;
            case IncidentKind.IonStorm:
                Notify($"Tempête ionique à {PlanetName(i.Planet)} : aucune flotte n'y entre ni n'en sort pendant " + Minutes(Galaxies.StormTime), i.Planet);
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
                Notify($"La tempête ionique de {PlanetName(i.Planet)} se dissipe", i.Planet);
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
                    notes.Add(("Bande errante", Sim.Battle.CountShips(i.Ships) > 0 ? $"Elle sort de l'ombre sur la voie vers {PlanetName(i.Other)}. Elle pille, ne prend rien."
                        : $"Elle se rassemble ici et frappera {PlanetName(i.Other)}. Elle pille, ne prend rien.", Palette.Of(Faction.Errant)));
                    break;
                case IncidentKind.WarBand when i.Other == p.Id:
                    notes.Add(("Bande errante en approche", Sim.Battle.CountShips(i.Ships) > 0 ? $"Elle arrive par la voie de {PlanetName(i.Planet)} : sans défense, la planète perdra ses mines."
                        : $"Elle se rassemble à {PlanetName(i.Planet)} : sans défense, la planète perdra ses mines.", Palette.Danger));
                    break;
                case IncidentKind.IonStorm when i.Planet == p.Id:
                    notes.Add(("Tempête ionique", $"Aucune flotte n'y entre ni n'en sort encore {Left(i.Remaining)} : les routes la contournent.", StormNote));
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
