using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using DevTerm.Core.Presenters;
using Microsoft.Extensions.Options;

namespace DevTerm.Presenters.Text;

/// <summary>
/// Decodes bytes as ASCII text, buffering internally until a line terminator (CR, LF, or CRLF —
/// treated as one terminator, not two) or <see cref="MaxLineLength"/> is reached, rather than
/// rendering whatever happened to arrive in a single read. Serial reads in particular routinely
/// deliver one byte at a time, which would otherwise mean one output line per character.
/// </summary>
/// <remarks>
/// Stateful, unlike the other text/numeric-base presenters — holds a partial line across calls.
/// One instance is meant to back one session's pipeline at a time; sharing an instance across
/// concurrent sessions would interleave their partial lines.
/// </remarks>
public sealed class AsciiPresenter : IPresenter, IPresenterInput
{
    public const int DefaultMaxLineLength = 4096;

    private const byte _lineFeed = (byte)'\n';
    private const byte _carriageReturn = (byte)'\r';

    private readonly List<byte> _buffer = [];
    private bool _pendingCr;

    /// <remarks>
    /// <see cref="AsciiPresenterOptions.MaxLineLength"/> flushes the accumulated line once it
    /// reaches that many bytes, even without a terminator. <c>0</c> means unbounded — wait for a
    /// terminator no matter how long the line gets.
    /// </remarks>
    public AsciiPresenter(IOptions<AsciiPresenterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var maxLineLength = options.Value.MaxLineLength;
        if (maxLineLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), maxLineLength, "MaxLineLength must be 0 (unbounded) or a positive maximum length.");
        }

        MaxLineLength = maxLineLength;
    }

    public int MaxLineLength { get; }

    public string Name => "ascii";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        List<string>? lines = null;

        foreach (var segment in data)
        {
            foreach (var b in segment.Span)
            {
                if (b == _lineFeed)
                {
                    if (_pendingCr)
                    {
                        // The second half of a CRLF pair already flushed by the CR — swallow it.
                        _pendingCr = false;
                        continue;
                    }

                    (lines ??= []).Add(Flush());
                    continue;
                }

                _pendingCr = false;

                if (b == _carriageReturn)
                {
                    (lines ??= []).Add(Flush());
                    _pendingCr = true;
                    continue;
                }

                _buffer.Add(b);
                if (MaxLineLength > 0 && _buffer.Count >= MaxLineLength)
                {
                    (lines ??= []).Add(Flush());
                }
            }
        }

        return lines ?? [];
    }

    public byte[] Parse(string input) => Encoding.ASCII.GetBytes(input);

    private string Flush()
    {
        var text = Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(_buffer));
        _buffer.Clear();
        return text;
    }
}
