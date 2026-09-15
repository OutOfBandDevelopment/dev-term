using HidSharp;

namespace DevTerm.Transports.Hid;

/// <summary>One HID device as seen during discovery, e.g. for a "--listhiddevices" CLI action or a GUI device picker.</summary>
public sealed record HidDeviceDescriptor(int VendorId, int ProductId, string? ProductName, string? SerialNumber);

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
        TryGet(device.GetSerialNumber));

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
