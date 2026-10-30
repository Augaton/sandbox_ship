// The battle HUD's selection panel: what is selected, its hull, shields, parts and state,
// the buttons that act on it, and the abilities of the selected ships. Port of
// refreshSelection, describe and abilityButtons in src/ui/hud.ts.
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Hud
{
    /// <summary>The selected own ships' state for one ability: how many are ready, how long it still runs, the shortest wait.</summary>
    sealed record AbilityGroup(AbilityId Id, int Ready, double Active, double Wait);

    void RefreshSelection()
    {
        scene.Selected.RemoveWhere(id => !World.Entities.Has(id));
        var ents = scene.Selected.Select(id => World.Entities.Get(id)!).ToList();
        List<Part> parts;
        if (ents.Count == 0)
        {
            parts =
            [
                Title("Aucune sélection"),
                TextPart("Clic gauche ou rectangle : sélectionner.\nClic droit : déplacer ou attaquer.\nAide : toutes les commandes.", Palette.Muted),
            ];
        }
        else if (ents.Count == 1)
        {
            parts = Describe(ents[0]);
        }
        else
        {
            var counts = new List<(string Type, int N, Entity E)>();
            foreach (var e in ents)
            {
                var i = counts.FindIndex(c => c.Type == e.Type);
                if (i >= 0) counts[i] = counts[i] with { N = counts[i].N + 1 };
                else counts.Add((e.Type, 1, e));
            }
            parts = [Title($"{ents.Count} unités"), Chips(counts)];
        }
        parts.AddRange(AbilityParts(ents));
        Sync(selection, parts);
    }

    static Part Title(string text) => new("title", () =>
    {
        var m = Ignore(new MarginContainer());
        m.AddThemeConstantOverride("margin_bottom", 6);
        m.AddChild(Text("", bold: true, wrap: true));
        return m;
    }, n => n.GetChild<Label>(0).Text = text);

    /// <summary>One button per class in a mixed selection: a click keeps only that class.</summary>
    Part Chips(List<(string Type, int N, Entity E)> counts) => new("chips:" + string.Join(",", counts.Select(c => c.Type)), () =>
    {
        var flow = Flow(4);
        foreach (var (type, _, e) in counts)
        {
            var b = Button("", () => scene.Selected.RemoveWhere(id => World.Entities.Get(id)?.Type != type), $"Garder seulement : {Names.EntityName(e)}", 32);
            flow.AddChild(b);
        }
        return flow;
    }, n =>
    {
        for (var i = 0; i < counts.Count; i++) n.GetChild<Button>(i).Text = $"{counts[i].N}× {Names.EntityName(counts[i].E)}";
    });

    /// <summary>A button of the panel, as wide as its text; wrapped when the panel is narrower.</summary>
    Part ActionPart(string key, string text, Action act, string? refused = null) => new(key, () => Button("", act), n =>
    {
        var b = (Button)n;
        b.Text = text;
        b.Disabled = refused is not null;
        b.TooltipText = refused ?? "";
        var width = b.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, b.GetThemeFontSize("font_size")).X + 18;
        var wrap = selection.Size.X > 0 && width > selection.Size.X;
        b.AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
        b.SizeFlagsHorizontal = wrap ? Control.SizeFlags.Fill : Control.SizeFlags.ShrinkBegin;
    });

    List<Part> Describe(Entity e)
    {
        var own = e.Faction == Player;
        var lines = new List<Part> { Title(Names.EntityName(e)) };
        if (e.Hero is HeroId hero) lines.Add(TextPart($"{Heroes.Defs[hero].Title}, {Heroes.Defs[hero].Description}", Palette.Muted));
        lines.Add(TextPart(Factions.Defs[e.Faction].Name + (own ? "" : " (ennemi)"), Palette.Muted));
        lines.Add(TextPart($"Coque {N(Math.Ceiling(e.Hull))} / {N(Js.Round(e.MaxHull))}"));
        lines.Add(BarPart(e.Hull / e.MaxHull, Palette.Ok));
        if (e.MaxShield > 0)
        {
            lines.Add(TextPart($"Bouclier {N(Math.Floor(e.Shield))} / {N(Js.Round(e.MaxShield))}"));
            lines.Add(BarPart(e.Shield / e.MaxShield, Palette.Shield));
        }
        if (e.Build < 1) lines.Add(TextPart($"En construction : {Percent(e.Build)}"));
        if (e.Type == "station")
        {
            lines.Add(TextPart($"Niveau {e.Level}, {Worlds.ChildrenOf(World, e.Id).Count} points d'attache"));
            if (own) lines.Add(TextPart("Clic droit : point de ralliement", Palette.Muted));
        }
        var tiers = e.Kind == EntityKind.Structure && Ids.IsBuildable(e.Type, out var type) ? Buildables.Defs[type].Tiers : null;
        if (tiers is not null)
        {
            lines.Add(TextPart($"Niveau {e.Level} / {tiers.Count + 1}"));
            if (own && e.Level <= tiers.Count)
            {
                var r = Structures.CanUpgradeStructure(World, Player, e.Id);
                var id = e.Id;
                lines.Add(ActionPart($"tier:{id}", $"Améliorer au niveau {e.Level + 1} ({N(tiers[e.Level - 1].Cost)} cr)",
                    () => Report(Structures.UpgradeStructure(World, Player, id)), r.Ok ? null : r.Reason));
                if (!r.Ok) lines.Add(TextPart(r.Reason, Palette.Muted));
            }
        }
        if (e.Hangar is { } hangar) lines.Add(TextPart($"Appareils lancés : {hangar.Launched.Count} / {hangar.Def.Cap}"));
        if (e.Income > 0) lines.Add(TextPart($"Revenu : +{N(e.Income)} cr/s"));
        if (e.Kind == EntityKind.Hardpoint && World.Entities.Get(e.ParentId) is { } parent)
        {
            lines.Add(TextPart($"Sur : {Names.EntityName(parent)}", Palette.Muted));
        }
        var slots = e.Kind == EntityKind.Ship ? Ships.Defs[e.Cls].Hardpoints : null;
        if (slots is not null)
        {
            var parts = Worlds.ChildrenOf(World, e.Id);
            var turrets = parts.Count(p => p.Weapons.Count > 0);
            var maxTurrets = slots.Count(s => s.Role is HardpointRole.Turret or HardpointRole.HeavyTurret);
            var status = new List<string> { $"tourelles {turrets} / {maxTurrets}" };
            if (e.MaxEngines > 0) status.Add($"réacteurs {e.Engines} / {e.MaxEngines}");
            lines.Add(TextPart($"Pièces : {string.Join(", ", status)}"));
            if (e.ShieldGens == 0) lines.Add(TextPart("Générateur de bouclier détruit"));
        }
        var fx = e.Effects;
        var states = new List<string>();
        if (fx.Marked > 0) states.Add($"marqué {N(Math.Ceiling(fx.Marked))} s");
        if (fx.Stunned > 0) states.Add($"paralysé {N(Math.Ceiling(fx.Stunned))} s");
        if (fx.Corroded > 0) states.Add($"rongé {N(Math.Ceiling(fx.Corroded))} s");
        if (fx.Taunted > 0) states.Add($"provoqué {N(Math.Ceiling(fx.Taunted))} s");
        if (e.ShieldDown > 0) states.Add($"bouclier rompu {N(Math.Ceiling(e.ShieldDown))} s");
        if (e.Expires is double expires) states.Add($"disparaît dans {N(Math.Ceiling(expires))} s");
        if (states.Count > 0) lines.Add(TextPart(string.Join(", ", states), Palette.Danger));
        if (!own)
        {
            foreach (var a in e.Abilities) lines.Add(TextPart($"Capacité : {Abilities.Defs[a.Id].Name}. {Abilities.Defs[a.Id].Description}", Palette.Muted));
        }
        if (e.Kind == EntityKind.Ship && own)
        {
            lines.Add(TextPart(e.Pilot is not null ? "Piloté" : e.Order is { } o ? OrderName(o.Type) : "En position", Palette.Muted));
            lines.Add(ActionPart("pilot", "Piloter (P)", scene.Controls.TogglePilot));
        }
        return lines;
    }

    /// <summary>
    /// One button per ability among the selected own ships: what it does, and whether it is
    /// running, ready or recharging. With "automatic" on, the ships use it by themselves.
    /// </summary>
    List<Part> AbilityParts(List<Entity> ents)
    {
        var byId = new List<AbilityGroup>();
        foreach (var e in ents)
        {
            if (e.Faction != Player) continue;
            foreach (var ab in e.Abilities)
            {
                var i = byId.FindIndex(s => s.Id == ab.Id);
                var a = i >= 0 ? byId[i] : new AbilityGroup(ab.Id, 0, 0, double.PositiveInfinity);
                a = ab.Cooldown == 0 ? a with { Ready = a.Ready + 1 } : a with { Wait = Math.Min(a.Wait, ab.Cooldown) };
                a = a with { Active = Math.Max(a.Active, ab.Active) };
                if (i >= 0) byId[i] = a;
                else byId.Add(a);
            }
        }
        if (byId.Count == 0) return [];
        var key = "abilities:" + string.Join(",", byId.Select(a => a.Id));
        return
        [
            GapPart(8),
            new Part(key, () =>
            {
                var box = Column(4);
                foreach (var a in byId)
                {
                    var def = Abilities.Defs[a.Id];
                    var id = a.Id;
                    // It reacts on press: the panel redraws every second with its cooldown.
                    var b = new LabelButton(true, 8, 2) { MinSize = new Vector2(0, 40), ActionMode = BaseButton.ActionModeEnum.Press };
                    b.Add(def.Name, Palette.Text, wrap: true);
                    b.Add("", Palette.Accent, 12, wrap: true);
                    b.Add(def.Description, Palette.Muted, 12, wrap: true);
                    b.Pressed += () => scene.Controls.UseAbilities(id);
                    box.AddChild(b);
                }
                box.AddChild(Check("Automatique", Settings.Current.AutoAbilities, on =>
                {
                    Settings.Current.AutoAbilities = on;
                    Settings.Current.Save();
                }, "Tes vaisseaux utilisent leurs capacités seuls quand c'est utile", 13, Palette.Muted));
                return box;
            }, n =>
            {
                var mode = scene.Controls.Mode;
                for (var i = 0; i < byId.Count; i++)
                {
                    var a = byId[i];
                    var def = Abilities.Defs[a.Id];
                    var aim = def.Target == AbilityTarget.Point ? ", puis clic où viser" : def.Target == AbilityTarget.Enemy ? ", puis clic sur un ennemi" : "";
                    var state = a.Active > 0 ? $"active encore {N(Math.Ceiling(a.Active))} s"
                        : a.Ready > 0 ? $"prête : E{aim}"
                        : $"recharge {N(Math.Ceiling(a.Wait))} s";
                    var aiming = mode is AbilityMode m && m.Id == a.Id;
                    var b = n.GetChild<LabelButton>(i);
                    b.Line(1).Text = aiming ? "clique pour viser, Échap pour annuler" : state;
                    b.Marked = aiming;
                    Outline(b, a.Active > 0 ? Palette.Ok : aiming ? Palette.Accent : null, false);
                    b.Paint();
                }
                n.GetChild<CheckBox>(byId.Count).SetPressedNoSignal(World.Factions[Player].AutoAbilities);
            }),
        ];
    }
}
