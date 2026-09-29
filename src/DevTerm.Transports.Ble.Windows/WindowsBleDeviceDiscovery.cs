using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace DevTerm.Transports.Ble.Windows;

/// <summary>
/// Lists nearby BLE peripherals via a live advertisement scan (<see cref="BluetoothLEAdvertisementWatcher"/>),
/// not Windows' paired-devices list. A prior version filtered on
/// <c>BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)</c>, which left plenty of real
/// peripherals invisible: cheap BLE UART modules (HC-08-family clones and similar) commonly use
/// "Just Works" pairing with no confirmation prompt, and Windows' Settings page can show one as
/// "Paired" without ever completing the underlying LE bond that selector actually checks for -
/// leaving it permanently unlistable despite being right there and advertising. Connecting doesn't
/// need pairing either: <see cref="WindowsBleAdapter"/>'s <c>BluetoothLEDevice.FromIdAsync</c>/GATT
/// calls work against an unpaired, unauthenticated peripheral, so there was never a real requirement
/// on the discovery side to match.
/// </summary>
public sealed class WindowsBleDeviceDiscovery : IBleDeviceDiscovery
{
    private static readonly TimeSpan _scanDuration = TimeSpan.FromSeconds(4);

    public IReadOnlyList<BleDeviceDescriptor> GetDevices()
    {
        var addresses = new ConcurrentDictionary<ulong, bool>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, args) => addresses.TryAdd(args.BluetoothAddress, true);

        watcher.Start();
        try
        {
            Task.Delay(_scanDuration).GetAwaiter().GetResult();
        }
        finally
        {
            watcher.Stop();
        }

        // Resolving each address to a BluetoothLEDevice (for its DeviceId/Name) is a second async
        // WinRT round-trip per device, done after the scan rather than inside the Received handler -
        // that handler fires once per advertisement (often several times per second per device while
        // scanning), and it only needs to record which addresses exist.
        var devices = new List<BleDeviceDescriptor>();
        foreach (var address in addresses.Keys)
        {
            using var device = BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask().GetAwaiter().GetResult();
            if (device is not null)
            {
                devices.Add(new BleDeviceDescriptor(device.DeviceId, device.Name is { Length: > 0 } name ? name : null));
            }
        }

        return devices;
    }
}
