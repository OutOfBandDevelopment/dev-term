namespace DevTerm.Transports.Rfc2217;

/// <summary>The one-line text for an RFC 2217 line or modem state notification, as shown on the status line and written to the session log.</summary>
internal static class Rfc2217StatusText
{
    /// <summary>The line-status bits worth telling the user about (errors and a break); register-empty bits are noise.</summary>
    public const Rfc2217LineState ReportedLineErrors =
        Rfc2217LineState.OverrunError | Rfc2217LineState.ParityError | Rfc2217LineState.FramingError | Rfc2217LineState.BreakDetect;

    public static string Modem(Rfc2217ModemState state) =>
        $"Modem lines: CTS {OnOff(state, Rfc2217ModemState.ClearToSend)}, DSR {OnOff(state, Rfc2217ModemState.DataSetReady)}, " +
        $"RI {OnOff(state, Rfc2217ModemState.RingIndicator)}, DCD {OnOff(state, Rfc2217ModemState.ReceiveLineSignalDetect)}";

    /// <summary>Null when no reported error bit is set.</summary>
    public static string? Line(Rfc2217LineState state)
    {
        var errors = new List<string>(4);
        if (state.HasFlag(Rfc2217LineState.OverrunError))
        {
            errors.Add("overrun");
        }

        if (state.HasFlag(Rfc2217LineState.ParityError))
        {
            errors.Add("parity");
        }

        if (state.HasFlag(Rfc2217LineState.FramingError))
        {
            errors.Add("framing");
        }

        if (state.HasFlag(Rfc2217LineState.BreakDetect))
        {
            errors.Add("break");
        }

        return errors.Count == 0 ? null : "Line error: " + string.Join(", ", errors);
    }

    private static string OnOff(Rfc2217ModemState state, Rfc2217ModemState bit) => state.HasFlag(bit) ? "on" : "off";
}
