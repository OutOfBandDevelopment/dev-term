namespace DevTerm.Transports.Ble;

/// <summary>
/// The per-connection seam a <see cref="BleTransport"/> talks to instead of a specific OS's native
/// BLE API directly - see docs/design/transports.md's "BLE" section for why (a pluggable per-OS
/// adapter behind one small contract, so a Linux/macOS backend can land later without touching
/// <see cref="BleTransport"/> or anything that references it). One instance represents one GATT
/// connection to one peripheral, already bound to the service/characteristic UUIDs from
/// <see cref="BleTransportOptions"/> at <see cref="ConnectAsync"/> time.
/// </summary>
public interface IBleAdapter : IAsyncDisposable
{
    /// <summary>Raised for each notification/indication received on the configured notify characteristic.</summary>
    event EventHandler<ReadOnlyMemory<byte>>? NotificationReceived;

    /// <summary>
    /// Raised when the peripheral drops the connection on its own (out of range, powered off, ...)
    /// rather than through <see cref="DisconnectAsync"/> - lets <see cref="BleTransport"/> report a
    /// lost connection the same way every other transport does instead of leaving callers reading
    /// from an <see cref="System.IO.Pipelines.PipeReader"/> that will never produce anything again.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>Connects to the peripheral and enables notifications on the configured notify characteristic.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes to the configured write characteristic.</summary>
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Disconnects deliberately (as opposed to <see cref="Disconnected"/>, which fires on an unexpected drop).</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
