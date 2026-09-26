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
public abstract class LineReplyPresenter : IPresenter, IStructuredPresenter, IReplyTracker
{
    private const byte _lineFeed = (byte)'\n';
    private const byte _carriageReturn = (byte)'\r';

    private readonly List<byte> _buffer = [];
    private readonly ConcurrentQueue<string> _pendingReplyIds = new();
    private bool _pendingCr;
    private bool _terminatorless;

    public abstract string Name { get; }

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    /// <summary>Whether each complete line is also returned from <see cref="Render"/> as output text (true by default).</summary>
    protected virtual bool RendersLines => true;

    public void QuerySent(string replyIndicatorId) => _pendingReplyIds.Enqueue(replyIndicatorId);

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
