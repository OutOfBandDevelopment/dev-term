using System.Net.Sockets;

namespace DevTerm.Configuration;

/// <summary>
/// Turns a connection failure into a clear message instead of a raw stack trace. Kept as a
/// pure function (no console I/O) so the wording is unit testable and reusable by any front end.
/// </summary>
public static class ConnectionErrorMessages
{
    public static string For(string transport, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var hint = transport.ToLowerInvariant() switch
        {
            "serial" => " Run with --listports to see available serial ports.",
            "hid" => " Run with --listhiddevices to see available USB HID devices.",
            _ => string.Empty,
        };

        return $"Could not open the connection: {exception.Message}{hint}";
    }

    /// <summary>
    /// Why a session disconnected on its own (<c>Session.Disconnected</c>): a read or send failure
    /// (<paramref name="error"/>), or the device closing the connection (<see langword="null"/>).
    /// Front ends report every lost connection through this one message, including a failed send
    /// (the session raises <c>Disconnected</c> before the send's exception reaches the caller), so
    /// the same failure is never reported twice. The flow-control hint only applies to serial: a
    /// timeout from USBTMC/HID/TCP has nothing to do with CTS.
    /// </summary>
    public static string ForDisconnect(string transport, Exception? error)
    {
        if (error is null)
        {
            return "The device closed the connection.";
        }

        if (error is TimeoutException && string.Equals(transport, "serial", StringComparison.OrdinalIgnoreCase))
        {
            return $"Connection lost: timed out - no response to hardware flow control (CTS)? Check the device or --handshake. ({error.Message})";
        }

        return $"Connection lost: {error.Message}";
    }

    /// <summary>
    /// Whether an exception from <c>Session.OpenAsync</c> represents an ordinary connection
    /// failure (bad port, port in use, host unreachable, a device that never answers, ...) that
    /// every front end should catch and report via <see cref="For"/>, rather than letting
    /// propagate as an unhandled crash. <see cref="TimeoutException"/> is included because the
    /// serial/HID transports raise it for a device that doesn't respond in time — it's not an
    /// <see cref="IOException"/>, so without it here a device timeout fell straight through every
    /// catch that filters on this method.
    /// </summary>
    public static bool IsConnectionFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or SocketException or TimeoutException;
}
