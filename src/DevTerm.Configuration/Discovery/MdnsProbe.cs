using System.Net;

namespace DevTerm.Configuration.Discovery;

/// <summary>
/// mDNS / DNS-SD: asks <c>224.0.0.251:5353</c> for the service types dev-term cares about and turns each answered instance
/// (PTR to SRV to A) into a hit. A real Rigol DG1062Z gave no mDNS reply earlier, so this is one probe among several.
/// Unit-tested against canned answers only; not yet checked against a real responder on the bench.
/// </summary>
public sealed class MdnsProbe(IUdpExchange exchange) : INetworkDeviceProbe
{
    public static readonly IReadOnlyList<string> ServiceTypes =
        ["_scpi-raw._tcp.local", "_lxi._tcp.local", "_vxi-11._tcp.local", "_mqtt._tcp.local", "_amqp._tcp.local", "_telnet._tcp.local", "_http._tcp.local"];

    public MdnsProbe() : this(new SystemUdpExchange())
    {
    }

    public string Name => "mdns";

    public async Task<IReadOnlyList<NetworkDeviceHit>> ProbeAsync(TimeSpan listenFor, CancellationToken cancellationToken = default)
    {
        var replies = await exchange.ExchangeAsync(new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), DnsMessage.BuildPtrQuery(ServiceTypes), listenFor, cancellationToken).ConfigureAwait(false);
        return Interpret(replies);
    }

    internal static IReadOnlyList<NetworkDeviceHit> Interpret(IEnumerable<UdpReply> replies)
    {
        var hits = new List<NetworkDeviceHit>();
        foreach (var reply in replies)
        {
            var records = DnsMessage.ParseRecords(reply.Data);
            var addresses = records.Where(r => r.Type == DnsMessage.TypeA).GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Target, StringComparer.OrdinalIgnoreCase);
            foreach (var ptr in records.Where(r => r.Type == DnsMessage.TypePtr && ServiceTypes.Contains(r.Name, StringComparer.OrdinalIgnoreCase)))
            {
                var srv = records.FirstOrDefault(r => r.Type == DnsMessage.TypeSrv && string.Equals(r.Name, ptr.Target, StringComparison.OrdinalIgnoreCase));
                if (srv is null)
                {
                    continue;
                }

                var address = addresses.GetValueOrDefault(srv.Target) ?? reply.From.ToString();
                var dot = ptr.Target.IndexOf('.', StringComparison.Ordinal);
                var instance = dot > 0 ? ptr.Target[..dot] : ptr.Target;
                hits.Add(new NetworkDeviceHit(address, srv.Port, "tcp", KindOf(ptr.Name), instance, "mdns", srv.Target));
            }
        }

        return hits;
    }

    private static string KindOf(string service) => service.Split('.')[0] switch
    {
        "_scpi-raw" or "_lxi" or "_vxi-11" => "lxi",
        "_mqtt" => "mqtt",
        "_amqp" => "amqp",
        _ => "unknown",
    };
}
