using System.IO.Ports;

namespace DevTerm.Configuration;

/// <summary>One human-readable line describing a connection, for a front end's "Connected to ..." status.</summary>
public static class ConnectionDescription
{
    public static string For(CliOptions cliOptions)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);

        if (string.Equals(cliOptions.Transport, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            return cliOptions.Listen
                ? $"TCP listener on port {cliOptions.TcpPort}"
                : $"TCP {cliOptions.Host}:{cliOptions.TcpPort}";
        }

        if (string.Equals(cliOptions.Transport, "hid", StringComparison.OrdinalIgnoreCase))
        {
            var serial = cliOptions.HidSerialNumber is null ? string.Empty : $" serial '{cliOptions.HidSerialNumber}'";
            return $"USB HID VID 0x{cliOptions.HidVendorId:X4} PID 0x{cliOptions.HidProductId:X4}{serial}";
        }

        var stopBits = cliOptions.StopBits switch
        {
            StopBits.One => "1",
            StopBits.Two => "2",
            StopBits.OnePointFive => "1.5",
            _ => cliOptions.StopBits.ToString(),
        };

        return $"{cliOptions.Port} at {cliOptions.Baud} baud ({cliOptions.DataBits}{cliOptions.Parity.ToString()[0]}{stopBits})";
    }

    /// <summary>
    /// The presenters displaying incoming data and the parser encoding typed lines, e.g.
    /// <c>ascii, hex; send as hex</c> — appended to a window title so it's always clear which
    /// formats are in play (the parser is per-line switchable, so it's passed in rather than read
    /// from <see cref="CliOptions.EffectiveParser"/>, which is only its starting value).
    /// </summary>
    public static string Formats(CliOptions cliOptions, string parser)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        return $"{string.Join(", ", cliOptions.EffectivePresenters)}; send as {parser}";
    }
}
