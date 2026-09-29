namespace DevTerm.Transports.Ble;

/// <summary>
/// One GATT characteristic under a <see cref="BleGattServiceDescriptor"/>, as seen during profile
/// enumeration (a "sub-device" picker for <see cref="BleTransportOptions.WriteCharacteristicUuid"/>/
/// <see cref="BleTransportOptions.NotifyCharacteristicUuid"/>) - not a live subscription, just a
/// snapshot of what the peripheral advertises it supports.
/// </summary>
public sealed record BleGattCharacteristicDescriptor(
    string Uuid,
    string? Name,
    bool CanRead,
    bool CanWrite,
    bool CanWriteWithoutResponse,
    bool CanNotify,
    bool CanIndicate);

/// <summary>One GATT service under a device, as seen during profile enumeration (see <see cref="IBleGattProfileExplorer"/>).</summary>
public sealed record BleGattServiceDescriptor(string Uuid, string? Name, IReadOnlyList<BleGattCharacteristicDescriptor> Characteristics);

/// <summary>
/// Enumerates a specific, already-discovered peripheral's GATT services/characteristics ("sub-device"
/// UUIDs) - a device picked via <see cref="IBleDeviceDiscovery"/> doesn't say which service/
/// characteristic UUIDs it actually exposes, and <see cref="BleTransportOptions"/> defaults to Nordic
/// UART Service's, which not every "BLE serial" peripheral actually uses. Separate from
/// <see cref="IBleDeviceDiscovery"/> since this needs a specific, already-known <c>deviceId</c>
/// (a GATT session against one peripheral) rather than scanning for nearby ones.
/// </summary>
public interface IBleGattProfileExplorer
{
    IReadOnlyList<BleGattServiceDescriptor> GetServices(string deviceId);
}

/// <summary>
/// The default registration when no per-OS BLE backend is available. Returns an empty list rather
/// than throwing, same reasoning as <see cref="UnsupportedPlatformBleDeviceDiscovery"/>.
/// </summary>
public sealed class UnsupportedPlatformBleGattProfileExplorer : IBleGattProfileExplorer
{
    public IReadOnlyList<BleGattServiceDescriptor> GetServices(string deviceId) => [];
}
