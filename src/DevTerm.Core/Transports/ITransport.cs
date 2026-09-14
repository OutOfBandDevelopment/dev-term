namespace DevTerm.Core.Transports;

/// <summary>
/// A byte- or message-oriented connection to a device. Implemented by transport plugins
/// (serial, TCP, UDP, USB HID, BLE, ...); the core never depends on a specific one.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    ConnectionState State { get; }

    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    event EventHandler<TransportDataReceivedEventArgs>? DataReceived;

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
