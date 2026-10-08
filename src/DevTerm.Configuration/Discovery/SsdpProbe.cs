using System.Net;
using System.Text;

namespace DevTerm.Configuration.Discovery;

/// <summary>
/// SSDP / UPnP: a multicast <c>M-SEARCH</c> to <c>239.255.255.250:1900</c>; every responder becomes a hit whose port is the one in its
/// <c>LOCATION</c> URL. Unit-tested against canned replies only; not yet checked against a real responder on the bench.
/// </summary>
public sealed class SsdpProbe(IUdpExchange exchange) : INetworkDeviceProbe
{
    public static readonly byte[] Request = Encoding.ASCII.GetBytes(
        "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: ssdp:all\r\n\r\n");

    public SsdpProbe() : this(new SystemUdpExchange())
    {
    }

    public string Name => "ssdp";

    public async Task<IReadOnlyList<NetworkDeviceHit>> ProbeAsync(TimeSpan listenFor, CancellationToken cancellationToken = default)
    {
        var replies = await exchange.ExchangeAsync(new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), Request, listenFor, cancellationToken).ConfigureAwait(false);
        return Interpret(replies);
    }

    internal static IReadOnlyList<NetworkDeviceHit> Interpret(IEnumerable<UdpReply> replies)
    {
        var hits = new List<NetworkDeviceHit>();
        foreach (var reply in replies)
        {
            var headers = Headers(Encoding.ASCII.GetString(reply.Data));
            if (headers is null)
            {
                continue;
            }

            var port = headers.TryGetValue("LOCATION", out var location) && Uri.TryCreate(location, UriKind.Absolute, out var uri) ? uri.Port : 80;
            var name = headers.GetValueOrDefault("SERVER") ?? headers.GetValueOrDefault("ST") ?? "UPnP device";
            hits.Add(new NetworkDeviceHit(reply.From.ToString(), port, "tcp", "upnp", name, "ssdp"));
        }

        return hits;
    }

    private static Dictionary<string, string>? Headers(string text)
    {
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("HTTP/1.1 200", StringComparison.Ordinal))
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        return headers;
    }
}
