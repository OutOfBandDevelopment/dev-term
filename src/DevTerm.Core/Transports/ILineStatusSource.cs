namespace DevTerm.Core.Transports;

/// <summary>
/// A transport that learns of serial line / modem status changes out of band (an RFC 2217 server's
/// <c>NOTIFY-LINESTATE</c>/<c>NOTIFY-MODEMSTATE</c>). Each notice is one line of human-readable text for the
/// status line and the session log; it never enters the data stream. <see cref="DevTerm.Core.Sessions.Session"/>
/// re-raises it as <see cref="DevTerm.Core.Sessions.Session.LineStatusChanged"/>.
/// </summary>
public interface ILineStatusSource
{
    /// <summary>Raised on a background thread with a notice such as <c>Modem lines: CTS on, DSR off, RI off, DCD off</c>.</summary>
    event EventHandler<string>? LineStatusChanged;
}
