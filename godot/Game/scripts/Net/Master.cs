// The online server as the game sees it apart from a game played through it: the multiplayer
// screen's list of games (MasterList), and a dedicated server's entry in that list (MasterListing,
// --public). Both keep one connection open and try again, gently, when the server goes away.
using Godot;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

/// <summary>The games the online server lists, asked for every few seconds, and whether it answers.</summary>
public sealed class MasterList
{
    /// <summary>Seconds between two lists, and before trying a server that did not answer again.</summary>
    const double Every = 4, Retry = 10;

    RelayLink? link;
    double asked, retry;

    public string Address { get; }
    public IReadOnlyList<OnlineGame> Games { get; private set; } = [];
    /// <summary>Why the server cannot be reached or refuses this game; null while it answers, or is being reached.</summary>
    public string? Problem { get; private set; }
    public bool Up => link?.Up ?? false;
    /// <summary>The round trip to the server, in ms; -1 while it is not up.</summary>
    public int Ping => link?.Ping ?? -1;

    public MasterList(string address, double now)
    {
        Address = address;
        Connect(now, false);
    }

    /// <summary>A new connection; `quiet`, tried again by itself: the reason the last one failed stays shown until this one is up.</summary>
    void Connect(double now, bool quiet)
    {
        link?.Close();
        link = RelayLink.Open(Address, new RelayFrame.List(Relay.Wire), out var error);
        (Problem, asked, retry) = (error ?? (quiet ? Problem : null), now, now + Retry);
    }

    /// <summary>The list again, now: the refresh button. A server that did not answer is tried again.</summary>
    public void Refresh(double now)
    {
        if (link is not { Down: false }) Connect(now, false);
        else if (link.Up)
        {
            link.Send(new RelayFrame.List(Relay.Wire));
            asked = now;
        }
    }

    /// <summary>What came since the last call; true when a list came (its pings with it), or whether the server answers changed.</summary>
    public bool Poll(double now)
    {
        var (up, problem, games, listed) = (Up, Problem, Games, false);
        link?.Poll(f =>
        {
            if (f is not RelayFrame.Games g) return;
            Games = g.Listed;
            listed = true;
        });
        if (link is { Up: true }) Problem = null;
        if (link is null || link.Down)
        {
            Problem ??= link?.Problem ?? $"Serveur en ligne injoignable{RelayLink.Shown(Address)}.";
            Games = [];
            if (now >= retry) Connect(now, true);
        }
        else if (link.Up && now - asked >= Every)
        {
            link.Send(new RelayFrame.List(Relay.Wire));
            asked = now;
        }
        return listed || Up != up || Problem != problem || !Games.SequenceEqual(games);
    }

    public void Close() => link?.Close();
}

/// <summary>A dedicated server's entry on the online server (--public): listed once connected, kept up to date, listed again if the server comes back.</summary>
public sealed class MasterListing(string master, string address)
{
    const double Retry = 15;

    RelayLink? link;
    LanGame? sent;
    bool playing, listed, told;
    double retry;

    public void Tick(double now, LanGame game, bool playing)
    {
        if (link is null || link.Down)
        {
            if (link is not null && !told)
            {
                told = true;
                Log(listed ? $"Inscription au serveur en ligne {master} perdue : nouvel essai dans {Retry} s."
                    : $"Serveur en ligne {master} injoignable{(link.Problem is { } why ? $" ({why})" : "")} : nouvel essai dans {Retry} s.");
            }
            if (now < retry) return;
            retry = now + Retry;
            link?.Close();
            link = RelayLink.Open(master, new RelayFrame.Open(Relay.Wire, game, true, address), out var error);
            (sent, this.playing, listed, told) = (game, false, false, false);
            if (error is not null) Log(error);
            return;
        }
        link.Poll(f =>
        {
            if (f is not RelayFrame.Opened o) return;
            listed = true;
            Log($"Inscrit au serveur en ligne {master} (n° {o.Room}) : la partie s'y affiche");
        });
        if (!listed || game == sent && playing == this.playing) return;
        (sent, this.playing) = (game, playing);
        link.Send(new RelayFrame.Update(game, playing));
    }

    public void Close() => link?.Close();

    static void Log(string text) => GD.Print($"[réseau] {text}");
}
