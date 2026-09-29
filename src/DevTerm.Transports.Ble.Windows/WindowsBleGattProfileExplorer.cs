using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace DevTerm.Transports.Ble.Windows;

/// <summary>
/// Enumerates a peripheral's full GATT profile (every service, every characteristic under it) via
/// <see cref="BluetoothLEDevice.GetGattServicesAsync()"/>/<see cref="GattDeviceService.GetCharacteristicsAsync()"/> -
/// unlike <see cref="WindowsBleAdapter"/>, which only looks up the one service/characteristic UUID
/// pair a connection is already configured with, this is the "what's actually on this device" picker
/// source. Uncached: a peripheral's GATT table can change between visits (firmware update, a
/// reconfigurable device), and this only runs on demand (a "Detect services..." click), not per
/// connection, so there's no hot path here to protect with a cache.
/// </summary>
public sealed class WindowsBleGattProfileExplorer : IBleGattProfileExplorer
{
    public IReadOnlyList<BleGattServiceDescriptor> GetServices(string deviceId)
    {
        using var device = BluetoothLEDevice.FromIdAsync(deviceId).AsTask().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"No BLE device found for id '{deviceId}'.");

        var servicesResult = device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
        if (servicesResult.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Failed to enumerate BLE services on device '{deviceId}': {servicesResult.Status}.");
        }

        var services = new List<BleGattServiceDescriptor>();
        foreach (var service in servicesResult.Services)
        {
            try
            {
                services.Add(new BleGattServiceDescriptor(FormatUuid(service.Uuid), null, GetCharacteristics(service)));
            }
            finally
            {
                service.Dispose();
            }
        }

        return services;
    }

    private static List<BleGattCharacteristicDescriptor> GetCharacteristics(GattDeviceService service)
    {
        var characteristics = new List<BleGattCharacteristicDescriptor>();
        var result = service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
        if (result.Status != GattCommunicationStatus.Success)
        {
            return characteristics;
        }

        foreach (var characteristic in result.Characteristics)
        {
            var props = characteristic.CharacteristicProperties;
            characteristics.Add(new BleGattCharacteristicDescriptor(
                FormatUuid(characteristic.Uuid),
                characteristic.UserDescription is { Length: > 0 } description ? description : null,
                props.HasFlag(GattCharacteristicProperties.Read),
                props.HasFlag(GattCharacteristicProperties.Write),
                props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse),
                props.HasFlag(GattCharacteristicProperties.Notify),
                props.HasFlag(GattCharacteristicProperties.Indicate)));
        }

        return characteristics;
    }

    // WindowsBleAdapter.ConnectAsync parses BleTransportOptions' UUID strings with Guid.Parse, so
    // format these the same way (Guid.ToString()'s default "D" format) rather than WinRT's own
    // ToString() - both render the same 36-character form for a standard GATT UUID, but staying on
    // Guid's own formatting keeps this independent of whatever WinRT happens to do.
    private static string FormatUuid(Guid uuid) => uuid.ToString();
}
