// The battle HUD over the network: no speed but ×1 and no sandbox; Esc opens a menu while the
// game goes on (Space pauses it for everyone); leaving means leaving the game, a player's side
// to the AI, or for a host, the game closed for all. The end of a battle brings everyone back
// to the lobby when the host, or a dedicated server's leader, says so. In a campaign battle,
// its commander may retreat or leave it to the AI, and one who watches goes back to the map.
using Godot;
using SandboxShip.Core;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Hud
{
    void NetButtons(HBoxContainer group)
    {
        group.AddChild(Button("Aide", () => ShowHelp()));
        group.AddChild(Button("Options", ShowOptions));
        group.AddChild(Button("Quitter", () => ConfirmLeave()));
    }

    /// <summary>Asks before leaving, saying what it does to the others.</summary>
    void ConfirmLeave(Action? back = null) =>
        Confirm("Quitter la partie ?", scene.Opts.Net!.Session.Hosting ? "La partie s'arrête pour tous les joueurs." : "L'IA prend ta place jusqu'à la fin.",
            "Quitter", scene.ToMenu, back);

    void NetMenu()
    {
        var session = scene.Opts.Net!.Session;
        var list = Column(8);
        list.CustomMinimumSize = new Vector2(240, 0);
        void Item(string label, Action act, bool primary = false)
        {
            var b = Button(label, act, "", 44);
            list.AddChild(primary ? Primary(b) : b);
        }
        Action back = NetMenu;
        Item("Reprendre", CloseModal, true);
        if (session.Leads && World.Winner is not null) Item("Retour au salon", session.BackToLobby);
        Item("Options", () => OpenModal(Options.Panel(CloseModal), back));
        Item("Commandes", () => ShowHelp(back));
        if (campaign is not null && scene.Opts.Net!.Watching) Item("Retour à la carte", scene.StopWatching);
        else if (campaign is not null)
        {
            if (scene.Opts.Campaign?.Attacker == Player) Item("Retraite", scene.Retreat);
            Item("Laisser l'IA finir", scene.Delegate);
        }
        Item("Quitter la partie", () => ConfirmLeave(back));
        OpenModal(Dialog("Menu", Para("La partie continue pendant ce menu : Espace la met en pause pour tous.", Palette.Muted, max: 320), list));
    }

    /// <summary>The end for everyone: the host, or a dedicated server's leader, brings all back to the lobby.</summary>
    void NetEnd(bool won)
    {
        var session = scene.Opts.Net!.Session;
        var watch = Button("Continuer à regarder", CloseModal);
        var parts = new List<Control> { Para(EndText(won), max: 672), EndTable() };
        if (session.Leads) parts.Add(Actions(Primary(Button("Retour au salon", session.BackToLobby)), watch));
        else
        {
            parts.Add(Para("L'hôte ramènera tout le monde au salon.", Palette.Muted));
            parts.Add(Actions(Button("Quitter la partie", scene.ToMenu), watch));
        }
        OpenModal(Dialog(EndTitle(won), [.. parts]));
    }
}
