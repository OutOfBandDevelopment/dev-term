namespace DevTerm.Transports.Tcp;

/// <summary>
/// Obtains an <see cref="ITcpConnection"/> for either TCP mode: dialing out (Client) or
/// accepting one inbound peer (Listener). See docs/design/transports.md.
/// </summary>
public interface ITcpConnectionSource
{
    Task<ITcpConnection> ConnectAsync(TcpTransportOptions options, CancellationToken cancellationToken);

    Task<ITcpConnection> AcceptAsync(TcpTransportOptions options, CancellationToken cancellationToken);
}
