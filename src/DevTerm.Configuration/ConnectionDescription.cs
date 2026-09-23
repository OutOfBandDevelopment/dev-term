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
            var serial = cliOptions.SerialNumber is null ? string.Empty : $" serial '{cliOptions.SerialNumber}'";
            return $"USB HID VID 0x{cliOptions.VendorId:X4} PID 0x{cliOptions.ProductId:X4}{serial}";
        }

        if (string.Equals(cliOptions.Transport, "usbtmc", StringComparison.OrdinalIgnoreCase))
        {
            var serial = cliOptions.SerialNumber is null ? string.Empty : $" serial '{cliOptions.SerialNumber}'";
            return $"USBTMC VID 0x{cliOptions.VendorId:X4} PID 0x{cliOptions.ProductId:X4}{serial}";
        }

        if (string.Equals(cliOptions.Transport, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            return "Loopback";
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
    /// A compact, URI-style description of a connection that isn't (or isn't known to be) a saved
    /// profile, for a window title: <c>tcp://192.168.0.110:23</c>, <c>serial://COM3:4800,8,n,1</c>,
    /// <c>hid://1A2B.C3D4.{serial number}</c> (vendor/product in hex, the serial number — the only
    /// per-device "instance" identifier a <see cref="CliOptions"/> carries — omitted when unset).
    /// </summary>
    public static string Definition(CliOptions cliOptions)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);

        if (string.Equals(cliOptions.Transport, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            return cliOptions.Listen
                ? $"tcp://*:{cliOptions.TcpPort} (listening)"
                : $"tcp://{cliOptions.Host}:{cliOptions.TcpPort}";
        }

        if (string.Equals(cliOptions.Transport, "hid", StringComparison.OrdinalIgnoreCase))
        {
            var instance = string.IsNullOrEmpty(cliOptions.SerialNumber) ? string.Empty : $".{cliOptions.SerialNumber}";
            return $"hid://{cliOptions.VendorId:X4}.{cliOptions.ProductId:X4}{instance}";
        }

        if (string.Equals(cliOptions.Transport, "usbtmc", StringComparison.OrdinalIgnoreCase))
        {
            var instance = string.IsNullOrEmpty(cliOptions.SerialNumber) ? string.Empty : $".{cliOptions.SerialNumber}";
            return $"usbtmc://{cliOptions.VendorId:X4}.{cliOptions.ProductId:X4}{instance}";
        }

        if (string.Equals(cliOptions.Transport, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            return "loopback://";
        }

        var stopBits = cliOptions.StopBits switch
        {
            StopBits.One => "1",
            StopBits.Two => "2",
            StopBits.OnePointFive => "1.5",
            _ => cliOptions.StopBits.ToString(),
        };

        return $"serial://{cliOptions.Port}:{cliOptions.Baud},{cliOptions.DataBits},{char.ToLowerInvariant(cliOptions.Parity.ToString()[0])},{stopBits}";
    }

    /// <summary>
    /// A main window's title: <c>dev-term — {name}</c> when <paramref name="cliOptions"/> is exactly a
    /// saved profile (see <see cref="ConnectionProfileStore.FindName"/>), otherwise
    /// <c>dev-term — {<see cref="Definition"/>}</c>, followed by <see cref="Formats"/> in parentheses.
    /// Recomputed by each front end whenever the connection or send format changes, so it follows a
    /// live profile switch instead of only describing whatever was launched.
    /// </summary>
    public static string WindowTitle(CliOptions cliOptions, string parser, ConnectionProfileStore profileStore)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        ArgumentNullException.ThrowIfNull(profileStore);
        var subject = profileStore.FindName(cliOptions) ?? Definition(cliOptions);
        return $"dev-term — {subject} ({Formats(cliOptions, parser)})";
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
