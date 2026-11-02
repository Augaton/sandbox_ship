// The chat over the battle view, in a game played over the network (ChatBox): its lines sit
// above the bottom panel, on the left, clear of the selection and of the build buttons.
namespace SandboxShip.Ui;

public partial class Hud
{
    /// <summary>Height kept free at the bottom of the screen: the bottom panel and the bar of groups above it.</summary>
    const float ChatClearance = 236;

    ChatBox? chat;

    /// <summary>Over the network: the chat, shown once.</summary>
    public void ShowChat()
    {
        if (chat is null) AddChild(chat = new ChatBox(false, ChatClearance));
    }
}
