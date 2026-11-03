/* A flat face of ENet for the online server (godot/Relay): what it needs of a host and its
 * peers, as plain functions over opaque pointers, so that C# mirrors no ENet structure. Built
 * with ENet 1.3.18 itself, the one Godot 4.7.2 embeds (build.sh): Godot's changes to it are its
 * sockets (IPv6, DTLS), not what goes on the wire.
 *
 * Compression is Godot's: ENet hands the commands of a packet as several buffers; they are put
 * end to end, as Godot does, and handed to the C# side's zstd (a standard frame), which a Godot
 * peer reads as its own, and whose frames it writes in turn. */
#include <stdlib.h>
#include <string.h>
#include <enet/enet.h>

/* Compresses or decompresses `inLength` bytes into at most `outLimit`; 0 when it cannot. */
typedef size_t (*rn_codec)(const enet_uint8 *in, size_t inLength, enet_uint8 *out, size_t outLimit);

typedef struct
{
    rn_codec compress, decompress;
    enet_uint8 *gathered;
    size_t gatheredSize;
} rn_compressor;

static size_t rn_compress(void *context, const ENetBuffer *inBuffers, size_t inBufferCount, size_t inLimit, enet_uint8 *outData, size_t outLimit)
{
    rn_compressor *c = (rn_compressor *) context;
    size_t i, at = 0;
    if (c->gatheredSize < inLimit)
    {
        enet_uint8 *grown = (enet_uint8 *) realloc(c->gathered, inLimit);
        if (grown == NULL) return 0;
        c->gathered = grown;
        c->gatheredSize = inLimit;
    }
    for (i = 0; i < inBufferCount && at < inLimit; ++i)
    {
        size_t n = inBuffers[i].dataLength;
        if (n > inLimit - at) n = inLimit - at;
        memcpy(c->gathered + at, inBuffers[i].data, n);
        at += n;
    }
    return c->compress(c->gathered, at, outData, outLimit);
}

static size_t rn_decompress(void *context, const enet_uint8 *inData, size_t inLimit, enet_uint8 *outData, size_t outLimit)
{
    return ((rn_compressor *) context)->decompress(inData, inLimit, outData, outLimit);
}

static void rn_compressor_destroy(void *context)
{
    rn_compressor *c = (rn_compressor *) context;
    free(c->gathered);
    free(c);
}

int rn_initialize(void) { return enet_initialize(); }

void rn_deinitialize(void) { enet_deinitialize(); }

/* A host on every address of this machine, at `port`. */
ENetHost *rn_host_create(enet_uint16 port, size_t peers, size_t channels)
{
    ENetAddress address;
    address.host = ENET_HOST_ANY;
    address.port = port;
    return enet_host_create(&address, peers, channels, 0, 0);
}

/* The host compresses what it sends and reads what comes compressed, with these two. */
int rn_host_compress(ENetHost *host, rn_codec compress, rn_codec decompress)
{
    ENetCompressor compressor;
    rn_compressor *c = (rn_compressor *) calloc(1, sizeof(rn_compressor));
    if (c == NULL) return -1;
    c->compress = compress;
    c->decompress = decompress;
    compressor.context = c;
    compressor.compress = rn_compress;
    compressor.decompress = rn_decompress;
    compressor.destroy = rn_compressor_destroy;
    enet_host_compress(host, &compressor);
    return 0;
}

void rn_host_destroy(ENetHost *host) { enet_host_destroy(host); }

void rn_host_flush(ENetHost *host) { enet_host_flush(host); }

/* Bytes received and sent on the wire since the last call, compressed. */
void rn_host_pop_totals(ENetHost *host, enet_uint32 *received, enet_uint32 *sent)
{
    *received = host->totalReceivedData;
    *sent = host->totalSentData;
    host->totalReceivedData = 0;
    host->totalSentData = 0;
}

/* One event: what happened, from which peer, on which channel; a packet's bytes and flags. */
typedef struct
{
    int type;
    ENetPeer *peer;
    int channel;
    int flags;
    ENetPacket *packet;
    const enet_uint8 *data;
    size_t length;
} rn_event;

/* The next event, waiting up to `timeout` ms for one: 1 with one, 0 with none, below 0 on an error. */
int rn_host_service(ENetHost *host, rn_event *out, enet_uint32 timeout)
{
    ENetEvent e;
    int got = enet_host_service(host, &e, timeout);
    if (got <= 0) return got;
    out->type = (int) e.type;
    out->peer = e.peer;
    out->channel = e.channelID;
    out->packet = e.packet;
    out->data = e.packet != NULL ? e.packet->data : NULL;
    out->length = e.packet != NULL ? e.packet->dataLength : 0;
    out->flags = e.packet != NULL ? (int) e.packet->flags : 0;
    return 1;
}

void rn_packet_destroy(ENetPacket *packet) { enet_packet_destroy(packet); }

/* Queues a copy of `data` for the peer: 0 when queued. */
int rn_peer_send(ENetPeer *peer, int channel, const enet_uint8 *data, size_t length, int flags)
{
    ENetPacket *packet = enet_packet_create(data, length, (enet_uint32) flags);
    if (packet == NULL) return -1;
    if (enet_peer_send(peer, (enet_uint8) channel, packet) < 0)
    {
        enet_packet_destroy(packet);
        return -1;
    }
    return 0;
}

int rn_peer_state(ENetPeer *peer) { return (int) peer->state; }

enet_uint32 rn_peer_round_trip(ENetPeer *peer) { return peer->roundTripTime; }

/* The peer's address as text into `text`, and its port. */
int rn_peer_address(ENetPeer *peer, char *text, size_t length)
{
    if (enet_address_get_host_ip(&peer->address, text, length) < 0) text[0] = '\0';
    return peer->address.port;
}

void rn_peer_timeout(ENetPeer *peer, enet_uint32 limit, enet_uint32 minimum, enet_uint32 maximum) { enet_peer_timeout(peer, limit, minimum, maximum); }

void rn_peer_ping_interval(ENetPeer *peer, enet_uint32 interval) { enet_peer_ping_interval(peer, interval); }

void rn_peer_disconnect_later(ENetPeer *peer) { enet_peer_disconnect_later(peer, 0); }

void rn_peer_disconnect_now(ENetPeer *peer) { enet_peer_disconnect_now(peer, 0); }
