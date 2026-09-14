namespace DevTerm.Console;

/// <summary>
/// Turns a connection failure into a clear CLI message instead of a raw stack trace. Kept as a
/// pure function (no console I/O) so the wording is unit testable.
/// </summary>
public static class ConnectionErrorMessages
{
    public static string For(string transport, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var hint = string.Equals(transport, "serial", StringComparison.OrdinalIgnoreCase)
            ? " Run with --listports to see available serial ports."
            : string.Empty;

        return $"Could not open the connection: {exception.Message}{hint}";
    }
}
