using DevTerm.Transports.Tcp;

namespace DevTerm.Configuration.Discovery;

/// <summary>The existing LXI discovery (VXI-11 portmapper broadcast, then <c>*IDN?</c> on the raw SCPI ports) as a probe.</summary>
public sealed class LxiProbe : INetworkDeviceProbe
{
    private readonly Func<TimeSpan, TimeSpan, CancellationToken, Task<IReadOnlyList<LxiDevice>>> _scan;

    public LxiProbe() : this((listen, probe, ct) => LxiDiscovery.ScanAsync(listen, probe, ct))
    {
    }

    public LxiProbe(Func<TimeSpan, TimeSpan, CancellationToken, Task<IReadOnlyList<LxiDevice>>> scan) => _scan = scan;

    public string Name => "lxi";

    public async Task<IReadOnlyList<NetworkDeviceHit>> ProbeAsync(TimeSpan listenFor, CancellationToken cancellationToken = default)
    {
        var devices = await _scan(listenFor, TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        return [.. devices.Select(d => d.ScpiPort > 0
            ? new NetworkDeviceHit(d.Host, d.ScpiPort, "tcp", "lxi", d.Identity, Name)
            : new NetworkDeviceHit(d.Host, d.Vxi11Port, "vxi11", "lxi", $"{d.Host} (VXI-11 only)", Name))];
    }
}
