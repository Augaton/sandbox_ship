// The menu's list of replays (Core/ReplayStore.cs): the battles last played from a lobby, newest
// first, each to watch again or to delete; a delete asks to be confirmed.
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public partial class Menu
{
    /// <summary>The replay whose "Supprimer" was pressed once: pressed again, it goes.</summary>
    string? confirmDelete;
    /// <summary>Why the replay last chosen could not be played.</summary>
    string? replayProblem;

    void ReplaysScreen()
    {
        Titled("Rediffusions", "Chaque bataille jouée depuis un salon s'enregistre ; les 30 dernières restent. Échap : retour");
        if (replayProblem is not null) body.AddChild(Paragraph(replayProblem, Palette.Danger, 14));
        var entries = ReplayStore.List();
        if (entries.Count == 0) body.AddChild(Paragraph("Aucune rediffusion pour l'instant : la prochaine bataille jouée apparaîtra ici.", Palette.Muted, 14));
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        foreach (var e in entries) list.AddChild(ReplayRow(e));
        body.AddChild(list);
        footer.AddChild(Btn("Retour", Back));
    }

    Control ReplayRow(ReplayStore.Entry e)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var key = $"replay:{System.IO.Path.GetFileName(e.Path)}";
        if (e.Header is { } r)
        {
            var title = ReplayStore.Title(r);
            var line = ReplayStore.Line(r) + (r.Version != Version ? $", version {r.Version}" : "");
            row.AddChild(Tile(key, false, () => Watch(e.Path), $"Revoir : {title}, {line}", col =>
            {
                col.AddChild(Strong(title, false));
                col.AddChild(Small(line));
            }));
        }
        else
        {
            var broken = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            broken.AddChild(Strong(System.IO.Path.GetFileName(e.Path), false));
            broken.AddChild(Small(e.Problem ?? "Fichier illisible."));
            row.AddChild(broken);
        }
        var confirm = confirmDelete == e.Path;
        var delete = Btn(confirm ? "Confirmer" : "Supprimer", () => Pick($"delete:{key}", () =>
        {
            if (confirmDelete != e.Path)
            {
                confirmDelete = e.Path;
                return;
            }
            confirmDelete = null;
            ReplayStore.Delete(e.Path);
        }), confirm ? Primary : null, $"delete:{key}");
        delete.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        delete.TooltipText = confirm ? "Supprimer pour de bon" : "Supprimer cette rediffusion";
        row.AddChild(delete);
        return row;
    }

    /// <summary>Plays a replay; one that cannot be read says why, at the top of the list.</summary>
    void Watch(string path)
    {
        if (ReplayStore.Load(path, out var problem) is { } replay)
        {
            replayProblem = null;
            Main.Instance.StartBattle(BattleOptions.Watch(replay));
            return;
        }
        replayProblem = problem;
        Rebuild($"replay:{System.IO.Path.GetFileName(path)}");
    }
}
