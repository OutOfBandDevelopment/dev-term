using DevTerm.Core.Presenters;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// Line-buffers incoming ASCII replies and correlates each complete line with the oldest pending
/// query (FIFO — good enough for SCPI's one-command-at-a-time model, not for interleaved concurrent
/// queries) registered via <see cref="LineReplyPresenter.QuerySent"/>. Implements <see cref="IStructuredPresenter"/> so
/// a generic control-panel renderer can drive a query's reply indicator without parsing this
/// presenter's own rendered text; an unsolicited line (empty queue) still renders as text, just with
/// no indicator update. See docs/design/features/scpi-instrument-control.md. The buffering and
/// correlation themselves live in the shared <see cref="LineReplyPresenter"/> (also the base of the
/// device-manifest reply presenter).
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
/// <see cref="LineReplyPresenter.ConfigureTerminator"/> switches this presenter into "terminator-less" mode, where
/// whatever bytes arrived in a single <see cref="LineReplyPresenter.Render"/> call are treated as one complete line as
/// soon as that call ends, instead of waiting for CR/LF — safe for Korad's short, atomically-read
/// replies (a handful of bytes for a query like <c>VOUT1?</c>).
/// </remarks>
public sealed class ScpiReplyPresenter : LineReplyPresenter, IScpiReplyTracker
{
    public override string Name => "scpi";
}
