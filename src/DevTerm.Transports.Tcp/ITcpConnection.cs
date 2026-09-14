namespace DevTerm.Transports.Tcp;

/// <summary>
/// One established TCP connection, however it was obtained (dialed out, or accepted from a
/// listener). Abstracted so <see cref="TcpTransport"/> can be unit tested with a fake instead
/// of a real socket.
/// </summary>
public interface ITcpConnection : IDisposable
{
    /// <summary>The connection's byte stream, pumped into a pipe (see <see cref="DevTerm.Core.Transports.StreamToPipePump"/>).</summary>
    Stream Stream { get; }

    void Write(byte[] buffer, int offset, int count);
}
