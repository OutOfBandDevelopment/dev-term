namespace DevTerm.Configuration.Discovery;

/// <summary>One device or service a discovery probe found, with what the Connection Editor needs to pre-fill a profile.</summary>
/// <param name="Address">Dotted IPv4 address.</param>
/// <param name="Port">The suggested port for <paramref name="Transport"/>.</param>
/// <param name="Transport">The suggested dev-term transport (<c>tcp</c>, <c>vxi11</c>, ...).</param>
/// <param name="Kind">What it is: <c>lxi</c>, <c>mqtt</c>, <c>upnp</c>, <c>unknown</c>, ... (later <c>usr-tcp232</c>, <c>ebyte-e810</c>).</param>
/// <param name="DisplayName">A human-readable label for the picker.</param>
/// <param name="Source">The name of the probe that found it.</param>
/// <param name="Hostname">The advertised host name, when the probe learned one.</param>
public sealed record NetworkDeviceHit(string Address, int Port, string Transport, string Kind, string DisplayName, string Source, string? Hostname = null)
{
    public string Display => $"{Address}  {DisplayName}  {Transport} :{Port}  ({Kind}, {Source})";
}

/// <summary>A single way of finding network devices (see docs/design/proposals/network-device-discovery.md).</summary>
public interface INetworkDeviceProbe
{
    string Name { get; }

    /// <summary>Looks for devices for up to <paramref name="listenFor"/>. Never throws for an unreachable network; returns what it found.</summary>
    Task<IReadOnlyList<NetworkDeviceHit>> ProbeAsync(TimeSpan listenFor, CancellationToken cancellationToken = default);
}
