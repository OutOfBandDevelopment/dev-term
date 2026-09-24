using HidSharp;

namespace DevTerm.Transports.Hid;

/// <summary>
/// One HID device as seen during discovery, e.g. for a "--listhiddevices" CLI action or a GUI device
/// picker. <paramref name="DevicePath"/> (below) is the OS's own per-connection instance path — unlike
/// <paramref name="SerialNumber"/>, every enumerated device has one (confirmed against a real Velleman
/// K8055: no serial descriptor and a blank product name, but a real, always-present device path), so
/// it's what actually tells two otherwise-identical devices (same VID/PID, no serial — the K8055's
/// board-address DIP switch makes this a real, not just theoretical, case) apart during one session.
/// Not persisted anywhere (<c>CliOptions</c> has no field for it) since it's tied to which USB port a
/// device is plugged into, not portable across reconnects/machines like VID/PID/serial are.
/// </summary>
public sealed record HidDeviceDescriptor(int VendorId, int ProductId, string? ProductName, string? SerialNumber, string DevicePath);

/// <summary>Enumerates connected HID devices.</summary>
public interface IHidDeviceDiscovery
{
    IReadOnlyList<HidDeviceDescriptor> GetDevices();
}

public sealed class SystemHidDeviceDiscovery : IHidDeviceDiscovery
{
    public IReadOnlyList<HidDeviceDescriptor> GetDevices() =>
        [.. DeviceList.Local.GetHidDevices().Select(ToDescriptor)];

    private static HidDeviceDescriptor ToDescriptor(HidDevice device) => new(
        device.VendorID,
        device.ProductID,
        TryGet(device.GetProductName),
        TryGet(device.GetSerialNumber),
        device.DevicePath);

    // Reading these strings from the device can fail (permissions, a device that doesn't
    // implement the optional string descriptors, ...) — discovery should still list the device
    // by VID/PID rather than fail the whole listing over a missing label.
    private static string? TryGet(Func<string> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
