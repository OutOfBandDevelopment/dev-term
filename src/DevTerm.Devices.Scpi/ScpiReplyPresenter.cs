using System.Buffers;
using System.Collections.Concurrent;
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
public sealed class ScpiReplyPresenter : IPresenter, IStructuredPresenter, IScpiReplyTracker
{
    private readonly List<byte> _buffer = [];
    private readonly ConcurrentQueue<string> _pendingReplyIds = new();

    public string Name => "scpi";

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    public void QuerySent(string replyIndicatorId) => _pendingReplyIds.Enqueue(replyIndicatorId);

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        foreach (var segment in data)
        {
            _buffer.AddRange(segment.Span);
        }

        var lines = new List<string>();
        int newlineIndex;
        while ((newlineIndex = _buffer.IndexOf((byte)'\n')) >= 0)
        {
            var lineLength = newlineIndex > 0 && _buffer[newlineIndex - 1] == (byte)'\r' ? newlineIndex - 1 : newlineIndex;
            var line = Encoding.ASCII.GetString(_buffer.GetRange(0, lineLength).ToArray());
            _buffer.RemoveRange(0, newlineIndex + 1);

            lines.Add(line);

            if (_pendingReplyIds.TryDequeue(out var replyId))
            {
                ValuesChanged?.Invoke(this, new Dictionary<string, string> { [replyId] = line });
            }
        }

        return lines;
    }
}
