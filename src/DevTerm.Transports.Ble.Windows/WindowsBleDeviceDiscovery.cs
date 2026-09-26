using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace DevTerm.Transports.Ble.Windows;

/// <summary>
/// Lists already-paired BLE peripherals via <see cref="DeviceInformation.FindAllAsync(string)"/>.
/// Deliberately paired-only, not a live advertisement scan (<c>BluetoothLEAdvertisementWatcher</c>)
/// - pairing is done once through Windows' own Bluetooth settings, then dev-term reconnects to a
/// known device by id, matching how a saved connection profile already expects a stable identifier
/// rather than re-discovering a device fresh on every connect.
/// </summary>
public sealed class WindowsBleDeviceDiscovery : IBleDeviceDiscovery
{
    public IReadOnlyList<BleDeviceDescriptor> GetDevices()
    {
        var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var devices = DeviceInformation.FindAllAsync(selector).AsTask().GetAwaiter().GetResult();
        return [.. devices.Select(d => new BleDeviceDescriptor(d.Id, d.Name))];
    }
}
