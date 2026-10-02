using System.Net;
using System.Net.Sockets;

namespace DevTerm.Transports.Tcp;

public sealed class SystemTcpConnectionSource : ITcpConnectionSource
{
    public async Task<ITcpConnection> ConnectAsync(TcpTransportOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(options.Host!, options.Port, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new SystemTcpConnection(client);
    }

    public async Task<ITcpConnection> AcceptAsync(TcpTransportOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var address = string.IsNullOrWhiteSpace(options.Host)
            ? IPAddress.IPv6Any
            : await ResolveBindAddressAsync(options.Host, cancellationToken).ConfigureAwait(false);
        var listener = new TcpListener(address, options.Port);
        if (address.Equals(IPAddress.IPv6Any))
        {
            // Unlike TcpListener.Create(port), the constructor does not enable dual-mode on its
            // own - without this, binding IPv6Any still only accepts IPv6 peers, which is no
            // better than the IPv4-only IPAddress.Any this replaces. See
            // docs/bugs/resolved/056-tcp-listen-rejects-hostnames.md.
            listener.Server.DualMode = true;
        }

        listener.Start();
        try
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            return new SystemTcpConnection(client);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Accepts an IP literal as-is; otherwise resolves it as a hostname. Listen mode's validators
    /// only require a non-empty host string, so a value like "localhost" reaches here -
    /// <see cref="IPAddress.Parse(string)"/> alone throws <see cref="FormatException"/> for that.
    /// See docs/bugs/resolved/056-tcp-listen-rejects-hostnames.md.
    /// </summary>
    private static async Task<IPAddress> ResolveBindAddressAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        return addresses.Length > 0
            ? addresses[0]
            : throw new SocketException((int)SocketError.HostNotFound);
    }
}
