using System.Net.Sockets;

namespace DevTerm.Transports.Tcp;

/// <summary>
/// <see cref="ITcpConnection"/> backed by a real <see cref="TcpClient"/>. Deliberately thin —
/// logic worth unit testing belongs in <see cref="TcpTransport"/>, which depends on the
/// interface instead of this class.
/// </summary>
public sealed class SystemTcpConnection : ITcpConnection
{
    private readonly TcpClient _client;

    public SystemTcpConnection(TcpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        Stream = client.GetStream();
    }

    public Stream Stream { get; }

    public void Write(byte[] buffer, int offset, int count) => Stream.Write(buffer, offset, count);

    public void Dispose()
    {
        Stream.Dispose();
        _client.Dispose();
    }
}
