using DevTerm.Transports.Ble;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.BleDeviceOptions"/> — a peripheral found by a
/// front end's own BLE scan (see <see cref="ConnectionEditorViewModel.SetBleDeviceOptions"/>),
/// formatted for display alongside the platform-specific <see cref="DeviceId"/> that actually gets
/// written into <see cref="ConnectionEditorViewModel.BleDeviceId"/> when picked. Unlike
/// <see cref="HidDeviceOption"/>/<see cref="UsbtmcDeviceOption"/>, there's no vendor/product ID to
/// filter on — a BLE peripheral's advertisement carries a name (if any) and its address-derived id,
/// nothing else this view model can key a picker list on.
/// </summary>
public sealed record BleDeviceOption(string Display, string DeviceId, string? Name)
{
    public static BleDeviceOption FromDescriptor(BleDeviceDescriptor descriptor)
    {
        var display = string.IsNullOrEmpty(descriptor.Name) ? descriptor.DeviceId : $"{descriptor.Name}  ({descriptor.DeviceId})";
        return new BleDeviceOption(display, descriptor.DeviceId, descriptor.Name);
    }
}
