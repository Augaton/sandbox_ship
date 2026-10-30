// What this version adds to the campaign, as the HUD shows it: the diplomacy list in the drawer
// (truces with each rival), the dialog of a truce offered to the player, the messages of truces,
// random events and ruins, the notes they leave on a planet, and supply.
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

    /// <summary>Every other side but the player's allies, who need no truce.</summary>
    IEnumerable<Side> Rivals() => G.Factions.Keys.Where(f => !Galaxies.Allies(G, f, Player));

    TruceOffer? OfferFrom(Side rival) => G.Offers.Find(o => o.From == rival && o.To == Player);

    double Grudge(Side rival) => G.Factions[rival].AI?.Grudges.GetValueOrDefault(Player) ?? 0;

    /// <summary>Where the player stands with a rival.</summary>
    string Relation(Side rival)
    {
        var g = G;
        if (g.Factions[rival].Eliminated) return "Éliminée";
        if (Galaxies.TruceOf(g, Player, rival) is { } t) return $"Trêve : encore {Left(t.Remaining)}";
        if (OfferFrom(rival) is { } o) return $"Propose une trêve : réponse sous {Left(o.Remaining)}";
        var grudge = Grudge(rival);
        return grudge > 0 ? $"En guerre ; trêve rompue, refuse toute trêve encore {Left(grudge)}" : "En guerre";
    }

    /// <summary>The diplomacy list of the drawer: each rival with where things stand and what can be done.</summary>
    List<Part> DiplomacyParts()
    {
        var g = G;
        var parts = new List<Part>
        {
            HudKit.TextPart($"Une trêve dure {Minutes(Galaxies.TruceTime)} : aucun combat entre vous, ni sur les planètes ni en chemin, et aucune flotte "
                + "envoyée chez l'autre. Une faction l'accepte quand elle se sent la plus faible, ou qu'elle se bat déjà ailleurs. "
                + $"Rompue, elle refuse toute trêve pendant {Minutes(Galaxies.GrudgeTime)}. Jamais de trêve avec les Errants.", Palette.Muted, 13),
        };
        foreach (var r in Rivals())
        {
            var peace = Galaxies.AtPeace(g, Player, r);
            var offer = OfferFrom(r) is not null;
            var state = g.Factions[r].Eliminated ? "out" : peace ? "peace" : offer ? "offer" : "war";
            parts.Add(new Part($"rival:{r}:{state}", () => RivalRow(r, state), c =>
            {
                ((Label)c.FindChild("Relation", true, false)).Text = Relation(r);
                if (c.FindChild("Propose", true, false) is Button propose)
                {
                    var why = Grudge(r) > 0 ? "Elle n'a pas oublié la trêve rompue" : "";
                    propose.Disabled = why.Length > 0;
                    propose.TooltipText = why.Length > 0 ? why : $"Proposer {Minutes(Galaxies.TruceTime)} de trêve";
                }
            }));
        }
        var others = g.Truces.Where(t => t.A != Player && t.B != Player).ToList();
        if (others.Count > 0)
        {
            parts.Add(HudKit.TextPart("Trêves entre les autres : "
                + string.Join(", ", others.Select(t => $"{NameOf(t.A)} et {NameOf(t.B)} ({Left(t.Remaining)})")), Palette.Muted, 13));
        }
        return parts;
    }

    /// <summary>A rival in the diplomacy list: emblem, name, relation, and the buttons its state allows.</summary>
    Control RivalRow(Side r, string state)
    {
        var col = HudKit.Column(4);
        var head = HudKit.Row(8);
        head.AddChild(Emblem(r));
        var name = HudKit.Column(0);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.AddChild(Text(NameOf(r), bold: true));
        var relation = Text("", Palette.Muted, 13, wrap: true);
        relation.Name = "Relation";
        name.AddChild(relation);
        head.AddChild(name);
        col.AddChild(head);
        var buttons = HudKit.Row(6);
        Button Add(string text, Action act, bool primary = false)
        {
            var b = Btn(text, act, primary);
            b.CustomMinimumSize = new Vector2(0, 32);
            buttons.AddChild(b);
            return b;
        }
        switch (state)
        {
            case "war":
                Add("Proposer une trêve", () => Propose(r)).Name = "Propose";
                break;
            case "offer":
                Add("Accepter", () => Answer(r, true), primary: true);
                Add("Refuser", () => Answer(r, false));
                break;
            case "peace":
                Add("Rompre la trêve", () => ConfirmBreak(r));
                break;
        }
        if (buttons.GetChildCount() > 0) col.AddChild(buttons);
        return col;
    }

    void Propose(Side rival)
    {
        var r = scene.Sink.Send(new GalaxyCommand.OfferTruce(rival));
        // A truce signed says so itself (TruceSigned).
        if (!r.Ok) Notify($"{NameOf(rival)} refuse la trêve : {Lower(r.Reason)}");
        RefreshSoon();
    }

    void Answer(Side rival, bool yes)
    {
        if (yes)
        {
            var r = scene.Sink.Send(new GalaxyCommand.AnswerTruce(rival, true));
            if (!r.Ok) Notify($"L'offre de {NameOf(rival)} n'est plus valable");
        }
        else
        {
            scene.Sink.Send(new GalaxyCommand.AnswerTruce(rival, false));
            Notify($"Trêve de {NameOf(rival)} refusée");
        }
        RefreshSoon();
    }

    void ConfirmBreak(Side rival)
    {
        var (p, box) = Dialog();
        box.AddChild(Heading($"Rompre la trêve avec {NameOf(rival)} ?", rival, 22));
        box.AddChild(HudKit.Para($"Vos flottes pourront de nouveau se battre, sur les planètes comme en chemin. {NameOf(rival)} refusera alors "
            + $"toute trêve pendant {Minutes(Galaxies.GrudgeTime)}."));
        box.AddChild(Actions(Btn("Annuler", CloseModal), Btn("Rompre", () =>
        {
            CloseModal();
            scene.Sink.Send(new GalaxyCommand.BreakTruce(rival));
            RefreshSoon();
        }, primary: true)));
        OpenModal(p);
    }

    /// <summary>A rival offers the player a truce: answer now, or later from the diplomacy list.</summary>
    void ShowOffer(Side rival)
    {
        var (p, box) = Dialog();
        box.AddChild(Heading($"{NameOf(rival)} propose une trêve", rival, 22));
        box.AddChild(HudKit.Para($"{Minutes(Galaxies.TruceTime)} sans aucun combat entre vous, ni sur les planètes ni en chemin, et aucune flotte "
            + $"envoyée chez l'autre. L'offre tient {Left(Galaxies.OfferTime)} ; la liste Diplomatie (T) la garde jusque-là."));
        box.AddChild(Actions(
            Btn("Accepter", () =>
            {
                CloseModal();
                Answer(rival, true);
            }, primary: true),
            Btn("Refuser", () =>
            {
                CloseModal();
                Answer(rival, false);
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
            case TruceOffered o when o.To == player:
                Sfx.Play(Sound.Alert);
                Notify($"{NameOf(o.From)} propose une trêve de {Minutes(Galaxies.TruceTime)}");
                if (!DialogOpen) ShowOffer(o.From);
                break;
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
        return notes;
    }

    /// <summary>What the planet panel shows of DepthNotes and specials, so that it is rebuilt when they change.</summary>
    string DepthSignature(Planet p) => $"{p.Special}:{string.Join(",", DepthNotes(p).Select(n => n.Title))}";
}
