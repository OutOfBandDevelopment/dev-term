using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace DevTerm.Core.Presenters;

/// <summary>
/// Line-buffers incoming ASCII replies and correlates each complete line with the oldest pending
/// query (FIFO) registered via <see cref="QuerySent"/>, publishing it through
/// <see cref="IStructuredPresenter.ValuesChanged"/> under that query's reply id. The shared base of
/// <c>DevTerm.Devices.Scpi.ScpiReplyPresenter</c> and <c>DevTerm.DeviceManifests.ManifestReplyPresenter</c>
/// — a subclass adds its own values per line via <see cref="AddLineValues"/> (a manifest's response
/// patterns, say) and chooses whether the line also renders as ordinary output text.
/// </summary>
/// <remarks>
/// CR, LF, CRLF, or LFCR all count as one line terminator (real-hardware-confirmed: a Tektronix
/// TDS2024 terminates replies with a bare CR). A device whose replies have no terminator at all (a
/// Korad KA3005P/KA6003P) is handled by <see cref="ConfigureTerminator"/> with an empty terminator:
/// whatever arrived in one <see cref="Render"/> call then counts as one complete line.
/// </remarks>
public abstract class LineReplyPresenter : IPresenter, IStructuredPresenter, IReplyTracker, IResettablePresenter
{
    private const byte _lineFeed = (byte)'\n';
    private const byte _carriageReturn = (byte)'\r';

    // Matches AsciiPresenter.DefaultMaxLineLength - an unbounded buffer let a hinted binary block (a
    // screen dump with no CR/LF in it) or a wedged device grow this list forever. See
    // docs/bugs/resolved/006-reply-queue-desync.md.
    private const int _maxBufferLength = 4096;

    private readonly List<byte> _buffer = [];
    private readonly ConcurrentQueue<string> _pendingReplyIds = new();
    private bool _pendingCr;
    private bool _pendingLf;
    private bool _terminatorless;

    public abstract string Name { get; }

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    /// <summary>Whether each complete line is also returned from <see cref="Render"/> as output text (true by default).</summary>
    protected virtual bool RendersLines => true;

    /// <summary>Publishes values that didn't come from a reply line (the manifest editor's preview shows sample data this way).</summary>
    protected void PublishValues(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count > 0)
        {
            ValuesChanged?.Invoke(this, values);
        }
    }

    public void QuerySent(string replyIndicatorId) => _pendingReplyIds.Enqueue(replyIndicatorId);

    public void Cancel(string replyIndicatorId)
    {
        // Removes the most recently registered occurrence of this id - the one QuerySent just
        // added - rather than the oldest, since a caller cancels the query it just sent, not
        // necessarily an older one still legitimately pending ahead of it.
        var items = _pendingReplyIds.ToArray();
        for (var i = items.Length - 1; i >= 0; i--)
        {
            if (items[i] != replyIndicatorId)
            {
                continue;
            }

            _pendingReplyIds.Clear();
            for (var j = 0; j < items.Length; j++)
            {
                if (j != i)
                {
                    _pendingReplyIds.Enqueue(items[j]);
                }
            }

            return;
        }
    }

    /// <summary>Clears the pending-reply queue and any partial line — see docs/bugs/resolved/006-reply-queue-desync.md.</summary>
    public void Reset()
    {
        _buffer.Clear();
        _pendingCr = false;
        _pendingLf = false;
        _pendingReplyIds.Clear();
    }

    /// <summary>Whether the device's replies will ever contain a CR/LF terminator at all — an empty <paramref name="terminator"/> switches to one-line-per-read mode (see remarks).</summary>
    public void ConfigureTerminator(string terminator) => _terminatorless = string.IsNullOrEmpty(terminator);

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        var lines = new List<string>();

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

                    Complete(lines);
                    _pendingLf = true;
                    continue;
                }

                if (b == _carriageReturn)
                {
                    if (_pendingLf)
                    {
                        // The second half of an LF CR pair already flushed by the LF — swallow it,
                        // or it counts as a spurious extra empty line and consumes the next pending
                        // query's reply id. See docs/bugs/resolved/051-line-reply-lf-cr-two-lines.md.
                        _pendingLf = false;
                        continue;
                    }

                    Complete(lines);
                    _pendingCr = true;
                    continue;
                }

                _pendingCr = false;
                _pendingLf = false;
                _buffer.Add(b);
                if (_buffer.Count >= _maxBufferLength)
                {
                    // A safety flush, not the end of the reply: show the chunk but leave the reply id queued.
                    Complete(lines, endsReply: false);
                }
            }
        }

        if (_terminatorless && _buffer.Count > 0)
        {
            Complete(lines);
        }

        return RendersLines ? lines : [];
    }

    /// <summary>Adds any values <paramref name="line"/> carries beyond its correlated reply (none by default).</summary>
    protected virtual void AddLineValues(string line, Dictionary<string, string> values)
    {
    }

    private void Complete(List<string> lines, bool endsReply = true)
    {
        var line = Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(_buffer));
        _buffer.Clear();
        lines.Add(line);
        if (!endsReply)
        {
            // See docs/bugs/resolved/063-line-reply-overflow-flush-desyncs-pending-ids.md.
            return;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_pendingReplyIds.TryDequeue(out var replyId))
        {
            values[replyId] = line;
        }

        AddLineValues(line, values);
        if (values.Count > 0)
        {
            ValuesChanged?.Invoke(this, values);
        }
    }
}
