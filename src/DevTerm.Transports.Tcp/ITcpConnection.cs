namespace DevTerm.Transports.Tcp;

/// <summary>
/// One established TCP connection, however it was obtained (dialed out, or accepted from a
/// listener). Abstracted so <see cref="TcpTransport"/> can be unit tested with a fake instead
/// of a real socket.
/// </summary>
public interface ITcpConnection : IDisposable
{
    event EventHandler<TcpDataReceivedEventArgs>? DataReceived;

    /// <summary>Raised when the remote end closes the connection or the read loop faults.</summary>
    event EventHandler? Closed;

    void Write(byte[] buffer, int offset, int count);
}

public sealed class TcpDataReceivedEventArgs(ReadOnlyMemory<byte> data) : EventArgs
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}
