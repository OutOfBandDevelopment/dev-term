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
