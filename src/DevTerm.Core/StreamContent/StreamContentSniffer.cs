using System.Buffers.Binary;

namespace DevTerm.Core.StreamContent;

/// <summary>Where a <see cref="StreamContentSniffer.Find"/> match starts, and — for content wrapped in an IEEE 488.2 definite-length block (<c>#&lt;n&gt;&lt;length&gt;&lt;payload&gt;</c>) — how long the block header is and how many payload bytes follow it.</summary>
/// <param name="Kind">What the content was recognized as.</param>
/// <param name="Offset">Where the match begins in the scanned data (the <c>#</c> of a block header, otherwise the content's own first byte).</param>
/// <param name="HeaderLength">Bytes of block header to skip before the content itself starts; 0 when the content isn't block-wrapped.</param>
/// <param name="PayloadLength">The block's declared payload length, or <see langword="null"/> when the content isn't block-wrapped (its end has to be found some other way).</param>
public readonly record struct StreamContentMatch(StreamContentKind Kind, int Offset, int HeaderLength, long? PayloadLength);

/// <summary>The result of <see cref="StreamContentSniffer.ParseBlockHeader"/>.</summary>
public enum BlockHeaderStatus
{
    /// <summary>The data doesn't start with a definite-length block header.</summary>
    NotABlock,

    /// <summary>The data starts like a block header but ends before the header does.</summary>
    Incomplete,

    /// <summary>A complete, valid block header.</summary>
    Complete,
}

/// <summary>
/// Pure, allocation-free signature matching for the Stream Monitor (see
/// docs/design/proposals/stream-content-detection.md): recognizes PNG, JPEG, GIF, BMP and TIFF
/// magic bytes, PostScript's <c>%!PS</c> header (and the binary EPS preview header), PCL job
/// starts (<c>ESC E ESC …</c>, the PJL universal exit language <c>ESC%-12345X</c>, and the
/// HP-GL/2-in-PCL mode switch), HP-GL's two-letter mnemonic instructions, and SCPI/IEEE 488.2
/// definite-length blocks wrapping any of those. No claim of perfect detection: anything it
/// doesn't recognize is simply not a match, and falls back to whatever the ordinary text/hex
/// presenters already show.
/// </summary>
public static class StreamContentSniffer
{
    /// <summary>
    /// The most bytes any signature check here needs to look at (an 11-byte block header plus the
    /// payload signature it wraps, or a few HP-GL instructions). A streaming caller that found no
    /// match should carry at least this many trailing bytes over into its next scan, so a signature
    /// split across two reads is still found.
    /// </summary>
    public const int MaxSignatureLength = 64;

    private static readonly byte[] _pngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] _binaryEpsSignature = [0xC5, 0xD0, 0xD3, 0xC6];
    private static readonly byte[] _pjlUel ="\u001b%-12345X"u8.ToArray();

    // The HP-GL/HP-GL/2 instructions a real plot stream is made of - a two-uppercase-letter pair is
    // only counted as an instruction if it's one of these, which is what keeps ordinary uppercase
    // text ("OK;", "ERR;") from looking like a plot.
    private static readonly HashSet<int> _hpglMnemonics = BuildMnemonicSet(
    [
        "AA", "AC", "AD", "AR", "AT", "BP", "BR", "BZ", "CA", "CI", "CP", "CR", "CS", "CT", "DF", "DI",
        "DL", "DR", "DT", "DV", "EA", "EP", "ER", "ES", "EW", "FI", "FN", "FP", "FT", "IN", "IP", "IR",
        "IW", "LA", "LB", "LO", "LT", "NP", "NR", "OP", "PA", "PC", "PD", "PE", "PG", "PM", "PR", "PS",
        "PT", "PU", "PW", "RA", "RF", "RO", "RP", "RR", "RT", "SA", "SB", "SC", "SD", "SI", "SL", "SM",
        "SP", "SR", "SS", "ST", "SV", "TD", "TL", "TR", "UL", "VS", "WG", "WU", "XT", "YT",
    ]);

    private static readonly int _label = Mnemonic('L', 'B');
    private static readonly int _initialize = Mnemonic('I', 'N');
    private static readonly int _defaults = Mnemonic('D', 'F');

    /// <summary>
    /// Identifies content that starts exactly at the beginning of <paramref name="head"/> (HP-GL
    /// included — the caller is asserting this is the start of a reply). Returns
    /// <see langword="null"/> when nothing matches, including when <paramref name="head"/> is too
    /// short to tell yet.
    /// </summary>
    public static StreamContentKind? Identify(ReadOnlySpan<byte> head) => IdentifyAt(head, allowHpgl: true);

    /// <summary>
    /// Finds the earliest recognizable content anywhere in <paramref name="data"/>. Binary
    /// signatures and PostScript/PCL headers match at any offset; HP-GL (plain ASCII, so the
    /// easiest to mistake for ordinary text) only at a reply boundary — the start of
    /// <paramref name="data"/> when <paramref name="startIsBoundary"/>, or just after a CR/LF.
    /// </summary>
    public static StreamContentMatch? Find(ReadOnlySpan<byte> data, bool startIsBoundary)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var rest = data[i..];
            if (rest[0] == (byte)'#'
                && ParseBlockHeader(rest, out var headerLength, out var payloadLength) == BlockHeaderStatus.Complete
                && payloadLength > 0
                && IdentifyAt(rest[headerLength..], allowHpgl: true) is { } wrapped)
            {
                return new StreamContentMatch(wrapped, i, headerLength, payloadLength);
            }

            var atBoundary = i == 0 ? startIsBoundary : data[i - 1] is (byte)'\r' or (byte)'\n';
            if (IdentifyAt(rest, atBoundary) is { } kind)
            {
                return new StreamContentMatch(kind, i, 0, null);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses an IEEE 488.2 definite-length arbitrary block header — <c>#</c>, one digit
    /// <c>n</c> (1–9), then <c>n</c> digits giving the payload length — the framing SCPI
    /// instruments wrap binary replies (screen dumps, waveform data) in.
    /// </summary>
    public static BlockHeaderStatus ParseBlockHeader(ReadOnlySpan<byte> data, out int headerLength, out long payloadLength)
    {
        headerLength = 0;
        payloadLength = 0;

        if (data.IsEmpty || data[0] != (byte)'#')
        {
            return BlockHeaderStatus.NotABlock;
        }

        if (data.Length < 2)
        {
            return BlockHeaderStatus.Incomplete;
        }

        var digitCount = data[1] - '0';
        if (digitCount is < 1 or > 9)
        {
            return BlockHeaderStatus.NotABlock;
        }

        long length = 0;
        for (var i = 0; i < digitCount; i++)
        {
            if (2 + i >= data.Length)
            {
                return BlockHeaderStatus.Incomplete;
            }

            var digit = data[2 + i] - '0';
            if (digit is < 0 or > 9)
            {
                return BlockHeaderStatus.NotABlock;
            }

            length = (length * 10) + digit;
        }

        headerLength = 2 + digitCount;
        payloadLength = length;
        return BlockHeaderStatus.Complete;
    }

    private static StreamContentKind? IdentifyAt(ReadOnlySpan<byte> s, bool allowHpgl)
    {
        if (s.IsEmpty)
        {
            return null;
        }

        switch (s[0])
        {
            case 0x89 when s.StartsWith(_pngSignature):
                return StreamContentKind.Png;
            case 0xFF when s.Length >= 3 && s[1] == 0xD8 && s[2] == 0xFF:
                return StreamContentKind.Jpeg;
            case (byte)'G' when s.StartsWith("GIF87a"u8) || s.StartsWith("GIF89a"u8):
                return StreamContentKind.Gif;
            case (byte)'I' when s.StartsWith("II*\0"u8):
            case (byte)'M' when s.StartsWith("MM\0*"u8):
                return StreamContentKind.Tiff;
            case (byte)'B' when IsBmp(s):
                return StreamContentKind.Bmp;
            case (byte)'%' when s.StartsWith("%!PS"u8):
            case 0xC5 when s.StartsWith(_binaryEpsSignature):
                return StreamContentKind.PostScript;
            case 0x1B when IsPcl(s):
                return StreamContentKind.Pcl;
        }

        return allowHpgl && IsHpgl(s) ? StreamContentKind.Hpgl : null;
    }

    // "BM" alone is two ordinary letters, so the rest of the 14-byte file header and the start of
    // the DIB header have to be plausible too: zero reserved bytes, a known DIB header size, and a
    // pixel-data offset past both headers (and inside the file, when a size is given).
    private static bool IsBmp(ReadOnlySpan<byte> s)
    {
        if (s.Length < 18 || s[1] != (byte)'M')
        {
            return false;
        }

        var fileSize = BinaryPrimitives.ReadUInt32LittleEndian(s[2..]);
        var reserved = BinaryPrimitives.ReadUInt32LittleEndian(s[6..]);
        var pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(s[10..]);
        var dibHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(s[14..]);

        return reserved == 0
            && dibHeaderSize is 12 or 40 or 52 or 56 or 64 or 108 or 124
            && pixelOffset >= 14 + dibHeaderSize
            && (fileSize == 0 || fileSize >= pixelOffset);
    }

    // A lone ESC E is also a VT100 control (NEL), and ESC % selects a character set in ISO 2022, so
    // only a PCL-shaped *job start* counts: the PJL universal exit language, a reset followed
    // straight away by another PCL escape, or the switch into HP-GL/2 mode.
    private static bool IsPcl(ReadOnlySpan<byte> s)
    {
        if (s.StartsWith(_pjlUel))
        {
            return true;
        }

        if (s.Length >= 4 && s[1] == (byte)'E' && s[2] == 0x1B && s[3] is (byte)'&' or (byte)'*' or (byte)'(' or (byte)')' or (byte)'%' or (byte)'E')
        {
            return true;
        }

        return s.StartsWith("\u001b%0B"u8) || s.StartsWith("\u001b%1B"u8) || s.StartsWith("\u001b%-1B"u8);
    }

    // IN; or DF; (initialize/default - how nearly every real plot starts) as the first instruction,
    // or at least three consecutive recognized instructions each terminated by ';'.
    private static bool IsHpgl(ReadOnlySpan<byte> s)
    {
        var i = 0;
        var count = 0;
        while (count < 3 && i + 1 < s.Length)
        {
            if (!IsUpper(s[i]) || !IsUpper(s[i + 1]))
            {
                break;
            }

            var mnemonic = Mnemonic((char)s[i], (char)s[i + 1]);
            if (!_hpglMnemonics.Contains(mnemonic))
            {
                break;
            }

            i += 2;
            if (mnemonic == _label)
            {
                // A label's text runs to an ETX terminator, not ';' - it counts as an instruction,
                // but nothing after it is inspected.
                count++;
                break;
            }

            while (i < s.Length && s[i] is (>= (byte)'0' and <= (byte)'9') or (byte)'+' or (byte)'-' or (byte)'.' or (byte)',' or (byte)' ')
            {
                i++;
            }

            if (i >= s.Length || s[i] != (byte)';')
            {
                break;
            }

            i = SkipWhitespace(s, i + 1);
            count++;

            if (count == 1 && (mnemonic == _initialize || mnemonic == _defaults))
            {
                return true;
            }
        }

        return count >= 3;
    }

    private static int Mnemonic(char first, char second) => (first << 8) | second;

    private static HashSet<int> BuildMnemonicSet(string[] mnemonics) => [.. mnemonics.Select(m => Mnemonic(m[0], m[1]))];

    private static bool IsUpper(byte b) => b is >= (byte)'A' and <= (byte)'Z';

    private static int SkipWhitespace(ReadOnlySpan<byte> s, int i)
    {
        while (i < s.Length && s[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            i++;
        }

        return i;
    }
}
