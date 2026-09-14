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

        var address = string.IsNullOrWhiteSpace(options.Host) ? IPAddress.Any : IPAddress.Parse(options.Host);
        var listener = new TcpListener(address, options.Port);
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
}
