using System.Buffers.Binary;

namespace DevTerm.Core.StreamContent;

/// <summary>
/// Finds where a capture of one <see cref="StreamContentKind"/> ends, from the format's own
/// structure, so a complete image is saved the moment its last byte arrives instead of waiting out
/// an idle timeout: PNG's <c>IEND</c> chunk, JPEG's end-of-image marker (walking segments, so an
/// embedded EXIF thumbnail's own end marker isn't mistaken for it), GIF's trailer, BMP's and binary
/// EPS's header-declared sizes, PostScript's <c>%%EOF</c>/Ctrl-D, and a PJL-wrapped PCL job's
/// closing universal exit language. Stateful and incremental — each call resumes where the last
/// left off, so a large capture arriving in many small reads isn't rescanned from the start every
/// time. HP-GL, TIFF, a bare-reset PCL job and unrecognized data have no reliable in-band end and
/// get no finder (<see cref="For"/> returns <see langword="null"/>); those end on idle instead.
/// </summary>
public abstract class StreamContentEndFinder
{
    /// <summary>
    /// Returns the total length of the content once its end has arrived (always
    /// <c>&lt;= content.Length</c>), or <see langword="null"/> if it hasn't yet — or if the data
    /// turned out not to be structured the way the format requires, in which case it never will
    /// and the capture ends on idle like an unstructured one. <paramref name="content"/> must be
    /// the whole capture so far, starting at the content's first byte, and only ever grow between
    /// calls.
    /// </summary>
    public abstract long? FindEnd(ReadOnlySpan<byte> content);

    /// <summary>A finder for <paramref name="kind"/>, or <see langword="null"/> when that kind has no in-band end.</summary>
    public static StreamContentEndFinder? For(StreamContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        if (kind == StreamContentKind.Png)
        {
            return new PngEndFinder();
        }

        if (kind == StreamContentKind.Jpeg)
        {
            return new JpegEndFinder();
        }

        if (kind == StreamContentKind.Gif)
        {
            return new GifEndFinder();
        }

        if (kind == StreamContentKind.Bmp)
        {
            return new BmpEndFinder();
        }

        if (kind == StreamContentKind.Pcx)
        {
            return new PcxEndFinder();
        }

        if (kind == StreamContentKind.PostScript)
        {
            return new PostScriptEndFinder();
        }

        if (kind == StreamContentKind.Hpgl)
        {
            return new HpglEndFinder();
        }

        return kind == StreamContentKind.Pcl ? new PclEndFinder() : null;
    }

    /// <summary>A finder for content whose length is already known (a definite-length block's payload).</summary>
    public static StreamContentEndFinder FixedLength(long length) => new FixedLengthEndFinder(length);

    private sealed class FixedLengthEndFinder(long length) : StreamContentEndFinder
    {
        public override long? FindEnd(ReadOnlySpan<byte> content) => content.Length >= length ? length : null;
    }

    private sealed class PngEndFinder : StreamContentEndFinder
    {
        private long _position = 8;

        public override long? FindEnd(ReadOnlySpan<byte> content)
        {
            while (_position + 8 <= content.Length)
            {
                var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(content[(int)_position..]);
                var next = _position + 12 + chunkLength;
                if (content.Slice((int)_position + 4, 4).SequenceEqual("IEND"u8))
                {
                    return next <= content.Length ? next : null;
                }

                _position = next;
            }

            return null;
        }
    }

    private sealed class JpegEndFinder : StreamContentEndFinder
    {
        private long _position = 2;
        private bool _inEntropyData;
        private bool _invalid;

        public override long? FindEnd(ReadOnlySpan<byte> content)
        {
            while (!_invalid)
            {
                if (_inEntropyData)
                {
                    // Entropy-coded scan data: 0xFF is only a marker when it isn't followed by a
                    // stuffed 0x00 or a restart marker (0xD0-0xD7).
                    var i = _position;
                    var found = false;
                    while (i + 1 < content.Length)
                    {
                        if (content[(int)i] == 0xFF && content[(int)i + 1] is not (0x00 or (>= 0xD0 and <= 0xD7)))
                        {
                            found = true;
                            break;
                        }

                        i++;
                    }

                    if (!found)
                    {
                        _position = Math.Max(_position, i);
                        return null;
                    }

                    _position = i;
                    _inEntropyData = false;
                    continue;
                }

                if (_position + 2 > content.Length)
                {
                    return null;
                }

                if (content[(int)_position] != 0xFF)
                {
                    _invalid = true;
                    return null;
                }

                var marker = content[(int)_position + 1];
                if (marker == 0xFF)
                {
                    // A fill byte before the real marker.
                    _position++;
                    continue;
                }

                if (marker == 0xD9)
                {
                    return _position + 2;
                }

                if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                {
                    _position += 2;
                    continue;
                }

                if (_position + 4 > content.Length)
                {
                    return null;
                }

                var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content[((int)_position + 2)..]);
                _position += 2 + segmentLength;

                // Start of scan: its header is a normal segment, then entropy-coded data follows.
                _inEntropyData = marker == 0xDA;
            }

            return null;
        }
    }

    private sealed class GifEndFinder : StreamContentEndFinder
    {
        private long _position = -1;
        private bool _inSubBlocks;
        private bool _invalid;

        public override long? FindEnd(ReadOnlySpan<byte> content)
        {
            if (_position < 0)
            {
                if (content.Length < 13)
                {
                    return null;
                }

                _position = 13 + GlobalColorTableSize(content[10]);
            }

            while (!_invalid)
            {
                if (_inSubBlocks)
                {
                    if (_position >= content.Length)
                    {
                        return null;
                    }

                    var size = content[(int)_position];
                    _position += 1 + size;
                    _inSubBlocks = size != 0;
                    continue;
                }

                if (_position >= content.Length)
                {
                    return null;
                }

                switch (content[(int)_position])
                {
                    case 0x3B:
                        return _position + 1;

                    case 0x21:
                        // Extension: introducer, label, then data sub-blocks.
                        if (_position + 2 > content.Length)
                        {
                            return null;
                        }

                        _position += 2;
                        _inSubBlocks = true;
                        break;

                    case 0x2C:
                        // Image descriptor (10 bytes), optional local color table, LZW minimum code
                        // size (1 byte), then image data sub-blocks.
                        if (_position + 10 > content.Length)
                        {
                            return null;
                        }

                        var localTable = GlobalColorTableSize(content[(int)_position + 9]);
                        _position += 10 + localTable + 1;
                        _inSubBlocks = true;
                        break;

                    default:
                        _invalid = true;
                        break;
                }
            }

            return null;
        }

        private static int GlobalColorTableSize(byte packedFields) =>
            (packedFields & 0x80) != 0 ? 3 * (1 << ((packedFields & 0x07) + 1)) : 0;
    }

    // An instrument's plot finishes by stowing the pen: "SP0;" (select no pen). Everything after it (a
    // "READY;" prompt, say) is not part of the plot. Without this the only end is an idle gap, and a slow
    // link that stalls mid-plot gets one plot cut into several files.
    private sealed class HpglEndFinder : StreamContentEndFinder
    {
        private long _scanFrom;

        public override long? FindEnd(ReadOnlySpan<byte> content)
        {
            var window = content[(int)_scanFrom..];
            var at = window.IndexOf("SP0;"u8);
            if (at >= 0)
            {
                return _scanFrom + at + 4;
            }

            _scanFrom = Math.Max(_scanFrom, content.Length - 3);
            return null;
        }
    }

    private sealed class BmpEndFinder : StreamContentEndFinder
    {
        public override long? FindEnd(ReadOnlySpan<byte> content)
        {
            if (content.Length < 6)
            {
                return null;
            }

            var fileSize = BinaryPrimitives.ReadUInt32LittleEndian(content[2..]);
            return fileSize > 0 && content.Length >= fileSize ? fileSize : null;
        }
    }

    // PCX: a 128-byte header (window, planes, bytes per line), then run-length-coded scanlines - a byte with
    // its top two bits set is a run count (low six bits) for the next byte, anything else is one literal - and,
    // for a version-5 8-bit single-plane image, a 0x0C marker plus a 768-byte palette. The end is known only by
    // decoding the runs until the declared number of bytes has been produced.
    private sealed class PcxEndFinder : StreamContentEndFinder
    {
        private const int _headerLength = 128;
        private long _need = -1;
        private long _produced;
        private int _position = _headerLength;
        private bool _hasPalette;

        public override long? FindEnd(ReadOnlySpan<byte> content)
        {
            if (_need < 0)
            {
                if (content.Length < _headerLength)
                {
                    return null;
                }

                var lines = (long)BinaryPrimitives.ReadUInt16LittleEndian(content[10..]) - BinaryPrimitives.ReadUInt16LittleEndian(content[6..]) + 1;
                var perLine = (long)content[65] * BinaryPrimitives.ReadUInt16LittleEndian(content[66..]);
                if (lines <= 0 || perLine <= 0)
                {
                    return null;
                }

                _need = lines * perLine;
                _hasPalette = content[1] == 5 && content[3] == 8 && content[65] == 1;
            }

            while (_produced < _need)
            {
                if (_position >= content.Length)
                {
                    return null;
                }

                if (content[_position] >= 0xC0)
                {
                    if (_position + 1 >= content.Length)
                    {
                        return null;
                    }

                    _produced += content[_position] & 0x3F;
                    _position += 2;
                }
                else
                {
                    _produced++;
                    _position++;
                }
            }

            if (!_hasPalette)
            {
                return _position;
            }

            if (_position >= content.Length)
            {
                return null;
            }

            if (content[_position] != 0x0C)
            {
                return _position;
            }

            return content.Length >= _position + 769 ? _position + 769 : null;
        }
    }

    private sealed class PostScriptEndFinder : StreamContentEndFinder
    {
        private long _scanFrom;

        public override long? FindEnd(ReadOnlySpan<byte> content)
        {
            if (content.Length >= 4 && content[0] == 0xC5)
            {
                // Binary EPS: a 30-byte header giving the offset/length of its PostScript, WMF and
                // TIFF sections - the file ends where the last of them does.
                if (content.Length < 28)
                {
                    return null;
                }

                long end = 0;
                for (var field = 4; field < 28; field += 8)
                {
                    var offset = BinaryPrimitives.ReadUInt32LittleEndian(content[field..]);
                    var length = BinaryPrimitives.ReadUInt32LittleEndian(content[(field + 4)..]);
                    end = Math.Max(end, (long)offset + length);
                }

                return content.Length >= end ? end : null;
            }

            var window = content[(int)_scanFrom..];
            var eof = window.IndexOf("%%EOF"u8);
            var ctrlD = window.IndexOf((byte)0x04);
            if (eof >= 0 && (ctrlD < 0 || eof < ctrlD))
            {
                var end = _scanFrom + eof + 5;
                while (end < content.Length && content[(int)end] is (byte)'\r' or (byte)'\n')
                {
                    end++;
                }

                return end;
            }

            if (ctrlD >= 0)
            {
                return _scanFrom + ctrlD + 1;
            }

            _scanFrom = Math.Max(0, content.Length - 4);
            return null;
        }
    }

    private sealed class PclEndFinder : StreamContentEndFinder
    {
        private static readonly byte[] _uel = "\u001b%-12345X"u8.ToArray();
        private long _scanFrom = -1;
        private bool _raster;

        // ESC*rB / ESC*rC end raster graphics. A printer's job then usually closes with a form feed
        // (ESC&l0H, or FF) and a reset (ESC E), so those are part of the job and a capture ends after them.
        private long? FindRasterEnd(ReadOnlySpan<byte> content)
        {
            var from = (int)_scanFrom;
            var window = content[from..];
            var at = window.IndexOf("\u001b*rB"u8);
            var atC = window.IndexOf("\u001b*rC"u8);
            if (at < 0 || (atC >= 0 && atC < at))
            {
                at = atC;
            }

            if (at < 0)
            {
                _scanFrom = Math.Max(_scanFrom, content.Length - 3);
                return null;
            }

            var end = from + at + 4;
            while (end < content.Length)
            {
                var rest = content[end..];
                if (rest[0] == 0x0C)
                {
                    end++;
                }
                else if (rest[0] == 0x1B)
                {
                    if (rest.Length < 2)
                    {
                        return null;
                    }

                    if (rest[1] == (byte)'E')
                    {
                        return end + 2;
                    }

                    if (rest[1] != (byte)'&')
                    {
                        return end;
                    }

                    // ESC & l <digits> H
                    var i = 2;
                    if (rest.Length <= i)
                    {
                        return null;
                    }

                    if (rest[i] != (byte)'l')
                    {
                        return end;
                    }

                    i++;
                    while (i < rest.Length && rest[i] is >= (byte)'0' and <= (byte)'9')
                    {
                        i++;
                    }

                    if (i >= rest.Length)
                    {
                        return null;
                    }

                    if (rest[i] != (byte)'H')
                    {
                        return end;
                    }

                    end += i + 1;
                }
                else
                {
                    return end;
                }
            }

            // Everything so far is the job; a form feed/reset may still follow, so wait for the idle timeout
            // rather than cutting the capture here.
            return null;
        }

        public override long? FindEnd(ReadOnlySpan<byte> content)
        {
            if (_scanFrom < 0)
            {
                if (content.Length < _uel.Length)
                {
                    return null;
                }

                if (!content.StartsWith(_uel))
                {
                    // A job that starts with a bare reset or raster setup has no closing UEL; a raster
                    // job (a scope's LaserJet hard copy) ends at its end-graphics escape instead.
                    _raster = true;
                    _scanFrom = 0;
                }
                else
                {
                    _scanFrom = _uel.Length;
                }
            }

            if (_raster)
            {
                return FindRasterEnd(content);
            }

            if (_scanFrom >= content.Length)
            {
                return null;
            }

            var index = content[(int)_scanFrom..].IndexOf(_uel);
            if (index >= 0)
            {
                return _scanFrom + index + _uel.Length;
            }

            _scanFrom = Math.Max(_scanFrom, content.Length - (_uel.Length - 1));
            return null;
        }
    }
}
