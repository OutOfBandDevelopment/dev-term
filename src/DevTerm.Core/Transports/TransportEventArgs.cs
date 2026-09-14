namespace DevTerm.Core.Transports;

public sealed class TransportDataReceivedEventArgs(ReadOnlyMemory<byte> data) : EventArgs
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}

public sealed class ConnectionStateChangedEventArgs(ConnectionState previous, ConnectionState current) : EventArgs
{
    public ConnectionState Previous { get; } = previous;
    public ConnectionState Current { get; } = current;
}
