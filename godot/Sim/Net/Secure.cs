// The game's messages sealed end to end, between a host and each of its players (not in the web
// version): straight or through the online server, which carries bytes it can neither read nor
// change unseen. Each connection opens with a handshake of its own, before any message:
//
//   host -> player   Offer    the format, whether the game is locked, its salt, the host's key
//   player -> host   Answer   the player's key, and its proof
//   host -> player   Accept   the host's proof; or Refuse, and why (a number, no free text)
//
// The keys are new for each connection (ECDH on P-256): both ends agree a secret, then HKDF
// draws from it a key for each direction and one for the proofs, its salt the password's key
// (Passwords.Key under the game's salt; zeros for an open game), its info a hash of the exchange.
// A proof is an HMAC of that exchange: the player's first, so that a host says nothing that
// depends on the password before the player has shown it holds it; and a player plays only once
// its host has. Every message then goes sealed by AES-GCM, its header (kind, counter) checked
// with it and its counter in the nonce: one counter per direction and per channel (reliable or
// not), never reused. A packet changed on its way, played again, or more than 64 behind the
// newest is left out. A large message is compressed before it is sealed: sealed bytes no longer
// compress on the wire.
//
// Against whom: one who listens on the way, the online server among them, reads nothing and
// changes nothing unseen. A locked game holds also against one in the middle (a server that
// would answer each end itself): without the password it can neither read nor pass for the host.
// It does not hold: an open game against one in the middle, who agrees a key with each end; and
// one in the middle of a locked game, given a player's proof, may try passwords against it
// offline, each try costing Passwords.Rounds of PBKDF2: a long password holds. The size and the
// pace of the packets stay visible.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SandboxShip.Sim.Net;

/// <summary>A game's secret, on its host: the salt its players make the password's key with, and that key (null for an open game).</summary>
public sealed class GameLock
{
    public const int SaltSize = 16;
    public readonly byte[] Salt;
    internal readonly byte[]? Key;

    GameLock(byte[] salt, byte[]? key) => (Salt, Key) = (salt, key);

    public bool Locked => Key is not null;

    /// <summary>A new game's lock: its password's key made once, here, for every player who joins.</summary>
    public static GameLock For(string? password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new GameLock(salt, password is null ? null : Passwords.Key(password, salt));
    }
}

/// <summary>What a handshake packet led to: bytes to send back, the channel ready, or why it closes (it is then of no use).</summary>
public readonly record struct Handshake(byte[]? Reply = null, bool Ready = false, string? Refused = null);

/// <summary>One end of one connection's sealed channel: its handshake, then its messages.</summary>
public sealed class SecureChannel : IDisposable
{
    /// <summary>The handshake's format, in the Offer: bumped with any change.</summary>
    public const int Format = 1;
    /// <summary>Bytes a sealed packet adds to its message: its kind, its counter, its tag.</summary>
    public const int Overhead = 1 + 8 + TagSize;
    /// <summary>The largest message sealed, and opened back from compressed bytes: a sealed one fits a relayed payload.</summary>
    public const int MaxMessage = Relay.MaxPayload - Overhead;
    /// <summary>Messages of this many bytes or more are compressed, when that makes them shorter.</summary>
    const int CompressFrom = 128;
    const int TagSize = 16, KeySize = 65, ProofSize = 32, NonceSize = 12, Window = 64;
    /// <summary>
    /// Brotli's quality and window for a message. Measured on the stress battle's snapshot parts
    /// (a kilobyte each): quality 3 leaves 2 % more bytes than ENet's zstd (level 3) did on them,
    /// for about as much time; 1 leaves 7 % more, 4 takes twice the time for 4 % fewer.
    /// </summary>
    const int Quality = 3, WindowBits = 22;

    enum Kind : byte { Offer = 0xE1, Answer, Accept, Refuse }
    /// <summary>A sealed packet's kind: this, bit 0 set on the unreliable channel, bit 1 when compressed.</summary>
    const byte Sealed = 0xD0;
    /// <summary>Why a host refuses a handshake, as it says it.</summary>
    enum Why : byte { Password = 1, Broken }
    enum State { WaitOffer, WaitAnswer, WaitAccept, Ready, Closed }

    static readonly byte[] Label = Encoding.ASCII.GetBytes("sandbox-ship/liaison/1");

    readonly bool host;
    readonly GameLock? game;
    readonly string? password;
    State state;
    ECDiffieHellman? mine;
    byte[] offer = [], transcript = [], proofs = [];
    AesGcm? sealer, opener;
    /// <summary>Messages sealed so far, by channel (0 reliable, 1 not): the next counter of each.</summary>
    readonly ulong[] sent = new ulong[2];
    /// <summary>By channel, the newest counter opened and which of the 64 before it were.</summary>
    readonly (ulong Top, ulong Seen, bool Any)[] opened = new (ulong, ulong, bool)[2];

    SecureChannel(bool host, GameLock? game, string? password, State state) => (this.host, this.game, this.password, this.state) = (host, game, password, state);

    /// <summary>
    /// Why this machine cannot seal, or null when it can: AES-GCM and ECDH come from the system's
    /// OpenSSL on Linux (libssl 3 or 1.1, which .NET looks for), from Windows itself on Windows.
    /// </summary>
    public static string? Missing
    {
        get
        {
            try
            {
                return AesGcm.IsSupported ? null : Unavailable;
            }
            catch (Exception e) when (e is TypeInitializationException or DllNotFoundException or PlatformNotSupportedException)
            {
                return Unavailable;
            }
        }
    }

    const string Unavailable = "Chiffrement indisponible sur cette machine : le jeu en réseau a besoin d'OpenSSL (libssl 3 ou 1.1).";

    public bool Ready => state == State.Ready;
    /// <summary>Packets left out: changed on the way, played again, too old, or come before the channel was ready.</summary>
    public int Rejected { get; private set; }

    /// <summary>A host's end, for a player that just connected: its offer goes first.</summary>
    public static SecureChannel Host(GameLock game, out byte[] offer)
    {
        var c = new SecureChannel(true, game, null, State.WaitAnswer) { mine = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) };
        offer = [(byte)Kind.Offer, Format, (byte)(game.Locked ? 1 : 0), .. game.Salt, .. PublicKey(c.mine)];
        c.offer = offer;
        return c;
    }

    /// <summary>A player's end, with the password it joins with (null: none); it waits for its host's offer.</summary>
    public static SecureChannel Player(string? password) => new(false, null, password, State.WaitOffer);

    /// <summary>A packet of the handshake, before the channel is ready; nothing once it is, or once it closed.</summary>
    public Handshake Take(byte[] packet)
    {
        if (state is State.Ready or State.Closed || packet.Length == 0) return default;
        try
        {
            return state switch
            {
                State.WaitAnswer => TakeAnswer(packet),
                State.WaitOffer => TakeOffer(packet),
                _ => TakeAccept(packet),
            };
        }
        catch (CryptographicException)
        {
            // A key that is no point of the curve.
            return host ? Refuse(Why.Broken) : Close("Échange illisible avec l'hôte : versions incompatibles ?");
        }
    }

    Handshake TakeAnswer(byte[] packet)
    {
        // Anything else before the answer is left out: the handshake's time runs (SecureLinks.Wait).
        if (packet[0] != (byte)Kind.Answer) return default;
        if (packet.Length != 1 + KeySize + ProofSize) return Refuse(Why.Broken);
        var theirs = packet.AsSpan(1, KeySize);
        Derive(theirs, game!.Key, theirs);
        if (!CryptographicOperations.FixedTimeEquals(packet.AsSpan(1 + KeySize), Proof(1))) return Refuse(game.Locked ? Why.Password : Why.Broken);
        state = State.Ready;
        return new Handshake([(byte)Kind.Accept, .. Proof(2)], true);
    }

    Handshake TakeOffer(byte[] packet)
    {
        const string Other = "L'hôte joue une autre version du jeu : il faut la même des deux côtés.";
        if (packet[0] != (byte)Kind.Offer || packet.Length < 2 || packet[1] != Format) return Close(Other);
        if (packet.Length != 3 + GameLock.SaltSize + KeySize || packet[2] > 1) return Close("Échange illisible avec l'hôte : versions incompatibles ?");
        var locked = packet[2] == 1;
        if (locked && password is null) return Close("Cette partie est protégée : saisis son mot de passe sur l'écran Multijoueur, puis rejoins-la.");
        // With a password, only a game locked by it: a host that says it has none could be anyone.
        if (!locked && password is not null) return Close("Cette partie ne demande pas de mot de passe : vide le champ du mot de passe, puis rejoins-la.");
        offer = packet;
        mine = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var answer = PublicKey(mine);
        Derive(packet.AsSpan(3 + GameLock.SaltSize), locked ? Passwords.Key(password!, packet[3..(3 + GameLock.SaltSize)]) : null, answer);
        state = State.WaitAccept;
        return new Handshake([(byte)Kind.Answer, .. answer, .. Proof(1)]);
    }

    Handshake TakeAccept(byte[] packet)
    {
        if (packet[0] == (byte)Kind.Refuse && packet.Length == 2)
        {
            return Close(packet[1] == (byte)Why.Password ? "Mot de passe incorrect." : "L'hôte a refusé la connexion : versions incompatibles ?");
        }
        if (packet[0] != (byte)Kind.Accept) return default;
        if (packet.Length != 1 + ProofSize || !CryptographicOperations.FixedTimeEquals(packet.AsSpan(1), Proof(2)))
        {
            return Close("L'hôte n'a pas prouvé qu'il connaît le mot de passe : la connexion est peut-être détournée.");
        }
        state = State.Ready;
        return new Handshake(Ready: true);
    }

    Handshake Refuse(Why why)
    {
        Close("");
        return new Handshake([(byte)Kind.Refuse, (byte)why], Refused: why == Why.Password ? "Mot de passe incorrect." : "Échange illisible.");
    }

    Handshake Close(string why)
    {
        state = State.Closed;
        Dispose();
        return new Handshake(Refused: why);
    }

    /// <summary>
    /// The keys, from this end's key and the other's, the password's key and the exchange: the
    /// offer as sent, then the player's key.
    /// </summary>
    void Derive(ReadOnlySpan<byte> theirs, byte[]? passwordKey, ReadOnlySpan<byte> answer)
    {
        using var other = Import(theirs);
        var shared = mine!.DeriveRawSecretAgreement(other.PublicKey);
        mine.Dispose();
        mine = null;
        using (var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            h.AppendData(Label);
            h.AppendData(offer);
            h.AppendData(answer);
            transcript = h.GetHashAndReset();
        }
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, shared, passwordKey ?? new byte[32]);
        var keys = HKDF.Expand(HashAlgorithmName.SHA256, prk, 96, transcript);
        // The host's messages to its player under the first key, the player's under the second.
        sealer = new AesGcm(keys.AsSpan(host ? 0 : 32, 32), TagSize);
        opener = new AesGcm(keys.AsSpan(host ? 32 : 0, 32), TagSize);
        proofs = keys[64..];
        CryptographicOperations.ZeroMemory(shared);
        CryptographicOperations.ZeroMemory(prk);
        CryptographicOperations.ZeroMemory(keys);
    }

    /// <summary>A proof of the exchange: 1 the player's, 2 the host's.</summary>
    byte[] Proof(byte who)
    {
        byte[] data = [who, .. transcript];
        return HMACSHA256.HashData(proofs, data);
    }

    static byte[] PublicKey(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        return [0x04, .. q.X!, .. q.Y!];
    }

    /// <summary>A key as sent (uncompressed point); CryptographicException for one that is not of the curve.</summary>
    static ECDiffieHellman Import(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize || key[0] != 0x04) throw new CryptographicException("Clé illisible");
        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33].ToArray(), Y = key[33..].ToArray() },
        });
    }

    /// <summary>A message sealed for the other end, on the reliable channel or not; its counter never used again.</summary>
    public byte[] Seal(byte[] message, bool reliable)
    {
        if (!Ready) throw new InvalidOperationException("Liaison pas encore établie");
        if (message.Length > MaxMessage) throw new ArgumentException($"Message de {message.Length} octets");
        var channel = reliable ? 0 : 1;
        var body = message.Length >= CompressFrom ? Compress(message) : null;
        var plain = body ?? message;
        var counter = sent[channel]++;
        var packet = new byte[Overhead + plain.Length];
        packet[0] = (byte)(Sealed | channel | (body is not null ? 2 : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(1, 8), counter);
        Span<byte> nonce = stackalloc byte[NonceSize];
        Nonce(nonce, host, channel, counter);
        sealer!.Encrypt(nonce, plain, packet.AsSpan(9, plain.Length), packet.AsSpan(9 + plain.Length), packet.AsSpan(0, 9));
        return packet;
    }

    /// <summary>The message a sealed packet holds; null for one changed on its way, played again, too old, or before the channel is ready.</summary>
    public byte[]? Open(byte[] packet)
    {
        if (!Ready || packet.Length < Overhead || (packet[0] & ~3) != Sealed) return Reject();
        var channel = packet[0] & 1;
        var counter = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(1, 8));
        if (!Fresh(channel, counter)) return Reject();
        var plain = new byte[packet.Length - Overhead];
        Span<byte> nonce = stackalloc byte[NonceSize];
        Nonce(nonce, !host, channel, counter);
        try
        {
            opener!.Decrypt(nonce, packet.AsSpan(9, plain.Length), packet.AsSpan(9 + plain.Length), plain, packet.AsSpan(0, 9));
        }
        catch (CryptographicException)
        {
            return Reject();
        }
        var message = (packet[0] & 2) != 0 ? Expand(plain) : plain;
        if (message is null) return Reject();
        Mark(channel, counter);
        return message;
    }

    byte[]? Reject()
    {
        Rejected++;
        return null;
    }

    /// <summary>The nonce of a counter: who seals (the host, or its player), the channel, then the counter.</summary>
    static void Nonce(Span<byte> nonce, bool fromHost, int channel, ulong counter)
    {
        nonce.Clear();
        nonce[0] = (byte)(fromHost ? 0 : 1);
        nonce[1] = (byte)channel;
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);
    }

    /// <summary>Whether a counter was never opened on its channel and is not more than Window behind the newest.</summary>
    bool Fresh(int channel, ulong counter)
    {
        var (top, seen, any) = opened[channel];
        if (!any || counter > top) return true;
        var behind = top - counter;
        return behind < Window && (seen & 1UL << (int)behind) == 0;
    }

    /// <summary>A counter opened: once it holds a real message, never before (a forged packet must not move the window).</summary>
    void Mark(int channel, ulong counter)
    {
        var (top, seen, any) = opened[channel];
        if (!any) opened[channel] = (counter, 1, true);
        else if (counter > top) opened[channel] = (counter, counter - top >= Window ? 1 : seen << (int)(counter - top) | 1, true);
        else opened[channel] = (top, seen | 1UL << (int)(top - counter), true);
    }

    /// <summary>A message compressed: its length first, then Brotli; null when that is no shorter.</summary>
    static byte[]? Compress(byte[] message)
    {
        var buffer = new byte[5 + BrotliEncoder.GetMaxCompressedLength(message.Length)];
        var head = 0;
        for (var n = (uint)message.Length; ; n >>= 7)
        {
            buffer[head++] = (byte)(n >= 0x80 ? n & 0x7F | 0x80 : n);
            if (n < 0x80) break;
        }
        if (!BrotliEncoder.TryCompress(message, buffer.AsSpan(head), out var written, Quality, WindowBits) || head + written >= message.Length) return null;
        return buffer[..(head + written)];
    }

    /// <summary>A compressed message back, at most MaxMessage bytes; null when it does not come back whole.</summary>
    static byte[]? Expand(byte[] body)
    {
        var (length, head) = (0, 0);
        for (var shift = 0; ; shift += 7)
        {
            if (head >= body.Length || shift > 21) return null;
            var b = body[head++];
            length |= (b & 0x7F) << shift;
            if (b < 0x80) break;
        }
        if (length > MaxMessage) return null;
        var message = new byte[length];
        return BrotliDecoder.TryDecompress(body.AsSpan(head), message, out var written) && written == length ? message : null;
    }

    public void Dispose()
    {
        mine?.Dispose();
        sealer?.Dispose();
        opener?.Dispose();
        (mine, sealer, opener) = (null, null, null);
        if (state == State.Ready) state = State.Closed;
    }
}

/// <summary>What came from a peer, its channel's handshake or a message: bytes to send back, the peer now known, a message, or why it goes.</summary>
public readonly record struct Arrival(byte[]? Reply = null, bool Came = false, byte[]? Message = null, string? Refused = null);

/// <summary>
/// The sealed channels of one end, by peer, as a transport keeps them: a host's with each of its
/// players, a player's with its host. A peer counts for the session once its channel is ready;
/// one whose handshake fails is told why (by a host) or hears it (a player); one whose handshake
/// takes too long goes, as does one refused, a moment later, its refusal on its way.
/// </summary>
public sealed class SecureLinks
{
    /// <summary>Seconds a handshake may take; seconds a refused peer keeps its connection, for the refusal to reach it.</summary>
    public const double Wait = 5, Grace = 0.5;

    readonly GameLock? game;
    readonly string? password;
    readonly Dictionary<int, (SecureChannel Channel, double Since)> links = [];
    readonly Dictionary<int, double> refused = [];

    SecureLinks(GameLock? game, string? password) => (this.game, this.password) = (game, password);

    /// <summary>A host's, for its game.</summary>
    public static SecureLinks Hosting(GameLock game) => new(game, null);

    /// <summary>A player's, with the password it joins with (null: none).</summary>
    public static SecureLinks Joining(string? password) => new(null, password);

    /// <summary>A host's: it offers each channel.</summary>
    public bool Hosts => game is not null;

    /// <summary>Packets left out on every channel: changed on the way, played again, too old.</summary>
    public int Rejected => links.Values.Sum(l => l.Channel.Rejected);

    /// <summary>A peer connected: what to send it first, reliably (a host's offer); null for a player, who waits for it.</summary>
    public byte[]? Connected(int peer, double now)
    {
        Gone(peer);
        byte[]? offer = null;
        var channel = game is not null ? SecureChannel.Host(game, out offer) : SecureChannel.Player(password);
        links[peer] = (channel, now);
        return offer;
    }

    public bool Ready(int peer) => links.TryGetValue(peer, out var l) && l.Channel.Ready;

    /// <summary>A packet from a peer: its handshake goes on, or its message is opened (nothing for a packet left out).</summary>
    public Arrival Received(int peer, byte[] packet, double now)
    {
        if (!links.TryGetValue(peer, out var l) || refused.ContainsKey(peer)) return default;
        if (l.Channel.Ready) return l.Channel.Open(packet) is { } message ? new Arrival(Message: message) : default;
        var h = l.Channel.Take(packet);
        if (h.Refused is not null) refused[peer] = now;
        return new Arrival(h.Reply, h.Ready, null, h.Refused);
    }

    /// <summary>A message sealed for a peer, reliably or not; null while its channel is not ready.</summary>
    public byte[]? Seal(int peer, byte[] message, bool reliable) =>
        links.TryGetValue(peer, out var l) && l.Channel.Ready ? l.Channel.Seal(message, reliable) : null;

    /// <summary>A peer gone: whether the session knew it (its channel was ready).</summary>
    public bool Gone(int peer)
    {
        refused.Remove(peer);
        if (!links.Remove(peer, out var l)) return false;
        var known = l.Channel.Ready;
        l.Channel.Dispose();
        return known;
    }

    /// <summary>
    /// Peers to let go now, forgotten here: their handshake took longer than Wait, or they were
    /// refused more than Grace ago. The session never knew them.
    /// </summary>
    public List<int> Overdue(double now)
    {
        var late = links.Where(l => !l.Value.Channel.Ready && !refused.ContainsKey(l.Key) && now - l.Value.Since > Wait).Select(l => l.Key)
            .Concat(refused.Where(r => now - r.Value > Grace).Select(r => r.Key)).ToList();
        foreach (var peer in late) Gone(peer);
        return late;
    }
}
