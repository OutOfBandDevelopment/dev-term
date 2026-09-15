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

        var stopBits = cliOptions.StopBits switch
        {
            StopBits.One => "1",
            StopBits.Two => "2",
            StopBits.OnePointFive => "1.5",
            _ => cliOptions.StopBits.ToString(),
        };

        return $"{cliOptions.Port} at {cliOptions.Baud} baud ({cliOptions.DataBits}{cliOptions.Parity.ToString()[0]}{stopBits})";
    }
}
