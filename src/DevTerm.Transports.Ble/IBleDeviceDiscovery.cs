namespace DevTerm.Transports.Ble;

/// <summary>
/// One BLE peripheral as seen during discovery, e.g. for a "--listbledevices" CLI action or a GUI
/// device picker. <paramref name="DeviceId"/> is whatever opaque identifier the underlying OS
/// backend needs to reconnect to this same peripheral later (a WinRT device id on Windows, a BlueZ
/// D-Bus object path on Linux, ...) - it's saved verbatim into <see cref="BleTransportOptions.DeviceId"/>,
/// never parsed or reconstructed by cross-platform code.
/// </summary>
public sealed record BleDeviceDescriptor(string DeviceId, string? Name);

/// <summary>Enumerates nearby/paired BLE peripherals.</summary>
public interface IBleDeviceDiscovery
{
    IReadOnlyList<BleDeviceDescriptor> GetDevices();
}

/// <summary>
/// The default registration when no per-OS BLE backend is available. Returns an empty list rather
/// than throwing - unlike <see cref="UnsupportedPlatformBleAdapterFactory"/>, a discovery listing is
/// informational (a picker, or "--listbledevices"), and an empty result is a legitimate, renderable
/// answer to "what's out there" the same way it would be on a real backend that just finds nothing.
/// </summary>
public sealed class UnsupportedPlatformBleDeviceDiscovery : IBleDeviceDiscovery
{
    public IReadOnlyList<BleDeviceDescriptor> GetDevices() => [];
}
