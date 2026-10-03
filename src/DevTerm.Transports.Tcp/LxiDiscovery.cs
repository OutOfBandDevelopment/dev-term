using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace DevTerm.Transports.Tcp;

/// <summary>One LAN instrument found by <see cref="LxiDiscovery"/>.</summary>
/// <param name="Host">Dotted IPv4 address.</param>
/// <param name="Vxi11Port">The port its VXI-11 core channel listens on (from the portmapper reply).</param>
/// <param name="ScpiPort">A raw-SCPI socket port that answered <c>*IDN?</c>, or 0 when none of the probed ports did.</param>
/// <param name="Identity">The <c>*IDN?</c> reply, or empty when no raw port answered.</param>
public sealed record LxiDevice(string Host, int Vxi11Port, int ScpiPort, string Identity)
{
    public string Display => Identity.Length > 0 ? $"{Identity}  ({Host}:{ScpiPort})" : $"{Host}  (VXI-11 only, no raw SCPI port found)";
}

/// <summary>
/// Finds LXI/VXI-11 instruments on the local network (docs/design/features/lxi-support.md, phase 1). Broadcasts an ONC-RPC
/// portmapper <c>GETPORT</c> for the VXI-11 core program to UDP 111 and keeps the hosts that answer with a non-zero port, then
/// asks each for <c>*IDN?</c> on the usual raw-SCPI ports so the result can feed the existing TCP transport directly.
/// mDNS (<c>_lxi._tcp</c>) was tried against a real Rigol DG1062Z and got no reply, so it is not used.
/// </summary>
public static class LxiDiscovery
{
    /// <summary>Raw SCPI socket ports tried, in order: the standard one, then Rigol's.</summary>
    public static readonly IReadOnlyList<int> ScpiPorts = [5025, 5555];

    private const int _portmapperPort = 111;
    private const uint _vxi11CoreProgram = 395183;

    /// <summary>The 56-byte portmapper GETPORT call for the VXI-11 core channel over TCP.</summary>
    public static byte[] BuildGetPortRequest(uint xid)
    {
        var message = new byte[56];
        var words = new uint[] { xid, 0, 2, 100000, 2, 3, 0, 0, 0, 0, _vxi11CoreProgram, 1, 6, 0 };
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(i * 4), words[i]);
        }

        return message;
    }

    /// <summary>The port in a GETPORT reply for <paramref name="xid"/>; null when it isn't an accepted reply or the program isn't registered (port 0).</summary>
    public static int? ParseGetPortReply(ReadOnlySpan<byte> reply, uint xid)
    {
        // xid, REPLY(1), MSG_ACCEPTED(0), verifier flavor, verifier length, accept_stat(0 = SUCCESS), port.
        if (reply.Length < 28 || BinaryPrimitives.ReadUInt32BigEndian(reply) != xid)
        {
            return null;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(reply[4..]) != 1 || BinaryPrimitives.ReadUInt32BigEndian(reply[8..]) != 0
            || BinaryPrimitives.ReadUInt32BigEndian(reply[20..]) != 0)
        {
            return null;
        }

        var port = BinaryPrimitives.ReadUInt32BigEndian(reply[24..]);
        return port is > 0 and <= 65535 ? (int)port : null;
    }

    /// <summary>Broadcasts on every up IPv4 interface and collects replies for <paramref name="listenFor"/>, then probes each host for a raw SCPI port.</summary>
    public static async Task<IReadOnlyList<LxiDevice>> ScanAsync(TimeSpan listenFor, TimeSpan probeTimeout, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, int>();
        foreach (var address in LocalIPv4Addresses())
        {
            await BroadcastFromAsync(address, listenFor, found, cancellationToken).ConfigureAwait(false);
        }

        var devices = await Task.WhenAll(found.OrderBy(kv => Version.Parse(kv.Key)).Select(kv => ProbeAsync(kv.Key, kv.Value, probeTimeout, cancellationToken))).ConfigureAwait(false);
        return devices;
    }

    private static IEnumerable<IPAddress> LocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);

    private static async Task BroadcastFromAsync(IPAddress local, TimeSpan listenFor, Dictionary<string, int> found, CancellationToken cancellationToken)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
            const uint xid = 0x44564D54;
            var request = BuildGetPortRequest(xid);
            await udp.SendAsync(request, new IPEndPoint(IPAddress.Broadcast, _portmapperPort), cancellationToken).ConfigureAwait(false);

            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(listenFor);
            while (true)
            {
                var reply = await udp.ReceiveAsync(window.Token).ConfigureAwait(false);
                if (ParseGetPortReply(reply.Buffer, xid) is int port)
                {
                    found[reply.RemoteEndPoint.Address.ToString()] = port;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The listen window closing is the normal end of a scan.
        }
        catch (SocketException)
        {
            // An interface that can't broadcast (VPN, virtual adapter) just contributes nothing.
        }
    }

    private static async Task<LxiDevice> ProbeAsync(string host, int vxiPort, TimeSpan timeout, CancellationToken cancellationToken)
    {
        foreach (var port in ScpiPorts)
        {
            if (await TryIdentifyAsync(host, port, timeout, cancellationToken).ConfigureAwait(false) is { Length: > 0 } identity)
            {
                return new LxiDevice(host, vxiPort, port, identity);
            }
        }

        return new LxiDevice(host, vxiPort, 0, string.Empty);
    }

    private static async Task<string?> TryIdentifyAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, window.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes("*IDN?\n"), window.Token).ConfigureAwait(false);
            var buffer = new byte[256];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), window.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
                if (buffer[length - 1] == (byte)'\n')
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(buffer, 0, length).Trim();
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
