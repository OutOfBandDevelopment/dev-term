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
/// CR, LF, or CRLF all count as one line terminator (real-hardware-confirmed: a Tektronix TDS2024
/// terminates replies with a bare CR). A device whose replies have no terminator at all (a Korad
/// KA3005P/KA6003P) is handled by <see cref="ConfigureTerminator"/> with an empty terminator:
/// whatever arrived in one <see cref="Render"/> call then counts as one complete line.
/// </remarks>
public abstract class LineReplyPresenter : IPresenter, IStructuredPresenter, IReplyTracker, IResettablePresenter
{
    private const byte _lineFeed = (byte)'\n';
    private const byte _carriageReturn = (byte)'\r';

    // Matches AsciiPresenter.DefaultMaxLineLength - an unbounded buffer let a hinted binary block (a
    // screen dump with no CR/LF in it) or a wedged device grow this list forever. See
    // docs/bugs/fixed/006-reply-queue-desync.md.
    private const int _maxBufferLength = 4096;

    private readonly List<byte> _buffer = [];
    private readonly ConcurrentQueue<string> _pendingReplyIds = new();
    private bool _pendingCr;
    private bool _terminatorless;

    public abstract string Name { get; }

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    /// <summary>Whether each complete line is also returned from <see cref="Render"/> as output text (true by default).</summary>
    protected virtual bool RendersLines => true;

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

    /// <summary>Clears the pending-reply queue and any partial line — see docs/bugs/fixed/006-reply-queue-desync.md.</summary>
    public void Reset()
    {
        _buffer.Clear();
        _pendingCr = false;
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
                    continue;
                }

                _pendingCr = false;

                if (b == _carriageReturn)
                {
                    Complete(lines);
                    _pendingCr = true;
                    continue;
                }

                _buffer.Add(b);
                if (_buffer.Count >= _maxBufferLength)
                {
                    Complete(lines);
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

    private void Complete(List<string> lines)
    {
        var line = Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(_buffer));
        _buffer.Clear();
        lines.Add(line);

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
