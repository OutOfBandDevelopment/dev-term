using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// Line-buffers incoming ASCII replies and correlates each complete line with the oldest pending
/// query (FIFO — good enough for SCPI's one-command-at-a-time model, not for interleaved concurrent
/// queries) registered via <see cref="QuerySent"/>. Implements <see cref="IStructuredPresenter"/> so
/// a generic control-panel renderer can drive a query's reply indicator without parsing this
/// presenter's own rendered text; an unsolicited line (empty queue) still renders as text, just with
/// no indicator update. See docs/design/proposals/scpi-instrument-control.md.
/// </summary>
/// <remarks>
/// Terminator handling mirrors <see cref="DevTerm.Presenters.Text.AsciiPresenter"/>: CR, LF, or
/// CRLF all count as one line terminator, not just LF. Confirmed against a real Tektronix TDS2024
/// over TCP — it terminates replies with a bare CR, so an earlier LF-only version of this presenter
/// never completed a line and the SCPI control panel's reply indicator never updated, even though
/// the same bytes rendered fine in plain ascii-presenter mode.
/// </remarks>
public sealed class ScpiReplyPresenter : IPresenter, IStructuredPresenter, IScpiReplyTracker
{
    private const byte LineFeed = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';

    private readonly List<byte> _buffer = [];
    private readonly ConcurrentQueue<string> _pendingReplyIds = new();
    private bool _pendingCr;

    public string Name => "scpi";

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    public void QuerySent(string replyIndicatorId) => _pendingReplyIds.Enqueue(replyIndicatorId);

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        var lines = new List<string>();

        foreach (var segment in data)
        {
            foreach (var b in segment.Span)
            {
                if (b == LineFeed)
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

                if (b == CarriageReturn)
                {
                    Complete(lines);
                    _pendingCr = true;
                    continue;
                }

                _buffer.Add(b);
            }
        }

        return lines;
    }

    private void Complete(List<string> lines)
    {
        var line = Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(_buffer));
        _buffer.Clear();
        lines.Add(line);

        if (_pendingReplyIds.TryDequeue(out var replyId))
        {
            ValuesChanged?.Invoke(this, new Dictionary<string, string> { [replyId] = line });
        }
    }
}
