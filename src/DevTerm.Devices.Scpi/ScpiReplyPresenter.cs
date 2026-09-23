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
///
/// This presenter is a single instance shared across whichever SCPI profile's control panel is
/// currently open (see <c>TuiMode.ResolveActiveScpiPresenter</c>/<c>MainWindow.ResolveActiveScpiPresenter</c>),
/// so it can't assume every instrument uses a CR/LF terminator at all — real-hardware-confirmed on a
/// Korad KA3005P/KA6003P, whose replies have no terminator whatsoever (see
/// <see cref="ScpiInstrumentProfile.Terminator"/> being <c>""</c> for those profiles). Buffering
/// forever waiting for a CR/LF that will never arrive meant a Korad control panel's reply
/// indicators never updated at all — the panel looked unresponsive even though the device was
/// replying correctly (confirmed independently via raw CLI + <c>--presenter hex</c>).
/// <see cref="ConfigureTerminator"/> switches this presenter into "terminator-less" mode, where
/// whatever bytes arrived in a single <see cref="Render"/> call are treated as one complete line as
/// soon as that call ends, instead of waiting for CR/LF — safe for Korad's short, atomically-read
/// replies (a handful of bytes for a query like <c>VOUT1?</c>).
/// </remarks>
public sealed class ScpiReplyPresenter : IPresenter, IStructuredPresenter, IScpiReplyTracker
{
    private const byte LineFeed = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';

    private readonly List<byte> _buffer = [];
    private readonly ConcurrentQueue<string> _pendingReplyIds = new();
    private bool _pendingCr;
    private bool _terminatorless;

    public string Name => "scpi";

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    public void QuerySent(string replyIndicatorId) => _pendingReplyIds.Enqueue(replyIndicatorId);

    /// <summary>
    /// Called whenever a control panel opens against a particular <see cref="ScpiInstrumentProfile"/>
    /// (mirrors <see cref="ScpiControlSurface"/>'s own per-profile construction) so this shared
    /// presenter knows whether that profile's replies will ever contain a CR/LF terminator at all.
    /// </summary>
    public void ConfigureTerminator(string terminator) => _terminatorless = string.IsNullOrEmpty(terminator);

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

        if (_terminatorless && _buffer.Count > 0)
        {
            Complete(lines);
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
