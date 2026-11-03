// Reading what the network or a file brings (not in the web version), never trusting it: every
// reader refuses broken bytes with InvalidDataException or EndOfStreamException, which their
// callers catch. BinaryReader's own 7-bit numbers throw FormatException on five bytes that never
// end one: a single packet of that kind took a server down.
namespace SandboxShip.Sim;

public static class Wire
{
    /// <summary>A 7-bit encoded number, as BinaryReader.Read7BitEncodedInt reads it; a malformed one throws InvalidDataException.</summary>
    public static int ReadVarint(this BinaryReader r)
    {
        uint result = 0;
        for (var shift = 0; shift < 28; shift += 7)
        {
            var b = r.ReadByte();
            result |= (b & 0x7Fu) << shift;
            if (b <= 0x7F) return (int)result;
        }
        // The fifth byte carries the last four bits, and nothing more.
        var last = r.ReadByte();
        if (last > 0b1111) throw new InvalidDataException("Nombre mal formé");
        return (int)(result | (uint)last << 28);
    }

    /// <summary>
    /// A text as BinaryWriter.Write(string) writes it (its length in bytes, 7-bit encoded, then
    /// UTF-8), at most `max` bytes: a longer or cut one is refused, nothing is allocated for a forged length.
    /// </summary>
    public static string ReadText(this BinaryReader r, int max = 4096)
    {
        var n = r.ReadVarint();
        if (n < 0 || n > max) throw new InvalidDataException($"Texte de {n} octets");
        var bytes = r.ReadBytes(n);
        if (bytes.Length != n) throw new EndOfStreamException();
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>A 7-bit encoded long, as BinaryReader.Read7BitEncodedInt64 reads it; a malformed one throws InvalidDataException.</summary>
    public static long ReadVarint64(this BinaryReader r)
    {
        ulong result = 0;
        for (var shift = 0; shift < 63; shift += 7)
        {
            var b = r.ReadByte();
            result |= (b & 0x7Ful) << shift;
            if (b <= 0x7F) return (long)result;
        }
        // The tenth byte carries the last bit, and nothing more.
        var last = r.ReadByte();
        if (last > 0b1) throw new InvalidDataException("Nombre mal formé");
        return (long)(result | (ulong)last << 63);
    }
}
