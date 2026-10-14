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

/// <summary>One physical device: every service discovery found at one address, shown as a row with a badge per service.</summary>
/// <param name="Address">Dotted IPv4 address shared by the services.</param>
/// <param name="Name">The best human-readable label among the services.</param>
/// <param name="Services">One hit per service (transport and port), ordered by port.</param>
public sealed record NetworkDevice(string Address, string Name, IReadOnlyList<NetworkDeviceHit> Services)
{
    /// <summary>The advertised host name, when any service learned one.</summary>
    public string? Hostname => Services.Select(s => s.Hostname).FirstOrDefault(h => !string.IsNullOrEmpty(h));

    /// <summary>A badge per service, e.g. <c>vxi11, tcp:5025, mqtt:1883</c>.</summary>
    public string Badges => string.Join(", ", Services.Select(s => s.Port > 0 ? $"{s.Transport}:{s.Port}" : s.Transport));
}

/// <summary>A single way of finding network devices (see docs/design/proposals/network-device-discovery.md).</summary>
public interface INetworkDeviceProbe
{
    string Name { get; }

    /// <summary>Looks for devices for up to <paramref name="listenFor"/>. Never throws for an unreachable network; returns what it found.</summary>
    Task<IReadOnlyList<NetworkDeviceHit>> ProbeAsync(TimeSpan listenFor, CancellationToken cancellationToken = default);
}
