using System.Buffers.Binary;
using System.Text;

namespace DevTerm.Test.Utilities;

/// <summary>
/// Small, structurally-valid samples of each format the Stream Monitor recognizes — hand-built so
/// every byte that matters to detection/end-finding is deliberate (PNG chunk CRCs are left zero:
/// nothing here validates them, and a decoder-backed test builds a real image instead). Shared by
/// the Core, Configuration, Console and WPF test projects.
/// </summary>
public static class StreamContentSamples
{
    /// <summary>A PNG: signature, IHDR, one IDAT, IEND.</summary>
    public static byte[] Png()
    {
        var bytes = new List<byte>();
        bytes.AddRange([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        bytes.AddRange(Chunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]));

        // Deliberately contains the letters "IEND" inside chunk data - only the real IEND chunk
        // (found by walking chunk lengths) ends the image.
        bytes.AddRange(Chunk("IDAT", [.. "xxIENDxx"u8]));
        bytes.AddRange(Chunk("IEND", []));
        return [.. bytes];
    }

    /// <summary>A JPEG with an EXIF-style APP1 segment carrying its own embedded SOI…EOI thumbnail, stuffed 0xFF00 and a restart marker in the scan data — only the final EOI ends it.</summary>
    public static byte[] Jpeg()
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]));
        bytes.AddRange(Segment(0xE1, [.. "Exif\0\0"u8, 0xFF, 0xD8, 0xFF, 0xD9]));
        bytes.AddRange(Segment(0xDB, [0, .. Enumerable.Repeat((byte)1, 64)]));
        bytes.AddRange(Segment(0xDA, [1, 1, 0, 0, 63, 0]));
        bytes.AddRange([0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF, 0xD0, 0x78, 0x9A]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    /// <summary>A GIF89a with a global color table, a graphic-control extension and one image whose LZW data contains a 0x3B byte (the trailer value) that must not end it.</summary>
    public static byte[] Gif()
    {
        var bytes = new List<byte>();
        bytes.AddRange("GIF89a"u8.ToArray());
        bytes.AddRange([1, 0, 1, 0, 0x80, 0, 0]);
        bytes.AddRange([0, 0, 0, 255, 255, 255]);
        bytes.AddRange([0x21, 0xF9, 4, 0, 0, 0, 0, 0]);
        bytes.AddRange([0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0]);
        bytes.AddRange([2, 2, 0x3B, 0x44, 0]);
        bytes.Add(0x3B);
        return [.. bytes];
    }

    /// <summary>A 2x2 24-bit BMP (54-byte headers + 16 bytes of padded pixel rows).</summary>
    public static byte[] Bmp()
    {
        var bytes = new byte[70];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), 70);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 24);
        for (var i = 54; i < 70; i++)
        {
            bytes[i] = (byte)(i * 7);
        }

        return bytes;
    }

    /// <summary>A little-endian TIFF header (no end marker - ends on idle).</summary>
    public static byte[] Tiff() => [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 0, 0];

    /// <summary>A short HP-GL plot.</summary>
    public static byte[] Hpgl() => Encoding.ASCII.GetBytes("IN;SP1;PU0,0;PD1000,0,1000,1000,0,1000,0,0;SP0;");

    /// <summary>A minimal PostScript document ending in <c>%%EOF</c>.</summary>
    public static byte[] PostScript() => Encoding.ASCII.GetBytes("%!PS-Adobe-3.0\n/Helvetica findfont 12 scalefont setfont\n72 72 moveto (hi) show\nshowpage\n%%EOF");

    /// <summary>A PJL-wrapped PCL job: universal exit language, a reset, some text, reset, closing UEL.</summary>
    public static byte[] PjlPcl() => Encoding.ASCII.GetBytes("\u001b%-12345X@PJL ENTER LANGUAGE=PCL\r\n\u001bE\u001b&l0OHello\u001bE\u001b%-12345X");

    /// <summary>Wraps <paramref name="payload"/> in an IEEE 488.2 definite-length block (<c>#&lt;n&gt;&lt;length&gt;</c>), the way a SCPI instrument returns a screen dump.</summary>
    public static byte[] ScpiBlock(byte[] payload)
    {
        var length = payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return [.. Encoding.ASCII.GetBytes($"#{length.Length}{length}"), .. payload];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Segment(byte marker, byte[] data)
    {
        var segment = new byte[4 + data.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(data.Length + 2));
        data.CopyTo(segment, 4);
        return segment;
    }
}
