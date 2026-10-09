using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace DevTerm.Configuration.Discovery;

/// <summary>
/// Finds USR-TCP232 serial-to-Ethernet bridges. The bridge sends no announcement (a UDP search broadcast got no
/// reply from a USR-TCP232-302 on the bench), but its web page on port 80 answers with a Basic-auth challenge whose
/// realm is the model name, e.g. <c>WWW-Authenticate: Basic realm="USR-TCP232-302"</c>. So this sweeps port 80 on each
/// local /24 and keeps the hosts whose realm starts with <c>USR-TCP232</c>. The suggested port is the bridge's default (23).
/// </summary>
public sealed partial class UsrBridgeProbe : INetworkDeviceProbe
{
    private const int _defaultBridgePort = 23;
    private readonly Func<IEnumerable<string>> _candidates;
    private readonly Func<string, TimeSpan, CancellationToken, Task<string?>> _fetchHeaders;

    public UsrBridgeProbe() : this(LocalSubnetHosts, FetchHeadersAsync)
    {
    }

    public UsrBridgeProbe(Func<IEnumerable<string>> candidates, Func<string, TimeSpan, CancellationToken, Task<string?>> fetchHeaders)
    {
        _candidates = candidates;
        _fetchHeaders = fetchHeaders;
    }

    public string Name => "usr";

    public async Task<IReadOnlyList<NetworkDeviceHit>> ProbeAsync(TimeSpan listenFor, CancellationToken cancellationToken = default)
    {
        using var throttle = new SemaphoreSlim(64);
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(listenFor.TotalMilliseconds / 3, 300, 1500));
        var hits = await Task.WhenAll(_candidates().Select(async host =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return ToHit(host, await _fetchHeaders(host, timeout, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                throttle.Release();
            }
        })).ConfigureAwait(false);
        return [.. hits.OfType<NetworkDeviceHit>()];
    }

    /// <summary>The hit for a host's HTTP response headers, or null when they aren't a USR-TCP232 bridge's.</summary>
    internal static NetworkDeviceHit? ToHit(string host, string? headers)
    {
        if (headers is null || RealmPattern().Match(headers) is not { Success: true } match)
        {
            return null;
        }

        var model = match.Groups["model"].Value;
        return new NetworkDeviceHit(host, _defaultBridgePort, "tcp", "usr-tcp232", $"{model} serial bridge", "usr");
    }

    private static IEnumerable<string> LocalSubnetHosts()
    {
        var own = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .ToList();
        foreach (var prefix in own.Select(a => a.GetAddressBytes()).Select(b => (b[0], b[1], b[2])).Distinct())
        {
            foreach (var last in Enumerable.Range(1, 254))
            {
                var host = $"{prefix.Item1}.{prefix.Item2}.{prefix.Item3}.{last}";
                if (!own.Any(a => a.ToString() == host))
                {
                    yield return host;
                }
            }
        }
    }

    private static async Task<string?> FetchHeadersAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, 80, window.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET / HTTP/1.0\r\nHost: {host}\r\n\r\n"), window.Token).ConfigureAwait(false);
            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer, window.Token).ConfigureAwait(false);
            return Encoding.ASCII.GetString(buffer, 0, read);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    [GeneratedRegex("""WWW-Authenticate:\s*Basic\s+realm="(?<model>USR-TCP232[^"]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex RealmPattern();
}
