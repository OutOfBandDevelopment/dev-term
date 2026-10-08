namespace DevTerm.Configuration.Discovery;

/// <summary>Runs every probe in parallel and merges the answers into one list ordered by address.</summary>
public sealed class NetworkDiscovery(IEnumerable<INetworkDeviceProbe> probes)
{
    private readonly IReadOnlyList<INetworkDeviceProbe> _probes = [.. probes];

    /// <summary>The probes dev-term ships: LXI, mDNS and SSDP.</summary>
    public static NetworkDiscovery CreateDefault() => new([new LxiProbe(), new MdnsProbe(), new SsdpProbe()]);

    public async Task<IReadOnlyList<NetworkDeviceHit>> DiscoverAsync(TimeSpan listenFor, CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(_probes.Select(p => RunAsync(p, listenFor, cancellationToken))).ConfigureAwait(false);
        return Merge(results.SelectMany(r => r));
    }

    /// <summary>Same address, port and transport collapse to one hit; the one with a specific kind and the longer name wins.</summary>
    public static IReadOnlyList<NetworkDeviceHit> Merge(IEnumerable<NetworkDeviceHit> hits) =>
        [.. hits.GroupBy(h => (h.Address, h.Port, h.Transport))
            .Select(g => g.OrderByDescending(h => h.Kind != "unknown").ThenByDescending(h => h.DisplayName.Length).First())
            .OrderBy(h => Version.TryParse(h.Address, out var v) ? v : new Version(0, 0))
            .ThenBy(h => h.Port)];

    private static async Task<IReadOnlyList<NetworkDeviceHit>> RunAsync(INetworkDeviceProbe probe, TimeSpan listenFor, CancellationToken cancellationToken)
    {
        try
        {
            return await probe.ProbeAsync(listenFor, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }
}
