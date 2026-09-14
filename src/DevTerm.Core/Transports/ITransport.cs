using System.IO.Pipelines;

namespace DevTerm.Core.Transports;

/// <summary>
/// A byte- or message-oriented connection to a device. Implemented by transport plugins
/// (serial, TCP, UDP, USB HID, BLE, ...); the core never depends on a specific one.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    ConnectionState State { get; }

    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Incoming bytes from the device. Backed by a <see cref="System.IO.Pipelines.Pipe"/> so a
    /// consumer (see <see cref="StreamToPipePump"/>) can read directly into the pipe's pooled
    /// buffers instead of the transport allocating and copying a new array per read. Valid once
    /// <see cref="State"/> reaches <see cref="ConnectionState.Open"/>.
    /// </summary>
    PipeReader Input { get; }

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
