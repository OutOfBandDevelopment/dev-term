using DevTerm.Transports.Hid;
using DevTerm.Transports.Serial;
using DevTerm.Transports.Usbtmc;

namespace DevTerm.Web;

/// <summary>What is plugged into the host right now (serial ports, USB HID and USBTMC devices), for <c>GET /api/devices</c>. A discovery failure yields an empty list for that kind, never an error.</summary>
public static class DeviceEnumeration
{
    public sealed record SerialEntry(string Port);

    public sealed record UsbEntry(string Id, int VendorId, int ProductId, string? Name, string? SerialNumber);

    public sealed record Result(IReadOnlyList<SerialEntry> Serial, IReadOnlyList<UsbEntry> Hid, IReadOnlyList<UsbEntry> Usbtmc);

    public static Result Enumerate(ISerialPortDiscovery? serial = null, IHidDeviceDiscovery? hid = null, IUsbtmcDeviceDiscovery? usbtmc = null) => new(
        Safe(() => (serial ?? new SystemSerialPortDiscovery()).GetPortNames().Select(p => new SerialEntry(p))),
        Safe(() => (hid ?? new SystemHidDeviceDiscovery()).GetDevices().Select(d => new UsbEntry($"{d.VendorId:X4}:{d.ProductId:X4}", d.VendorId, d.ProductId, d.ProductName, d.SerialNumber))),
        Safe(() => (usbtmc ?? new SystemUsbtmcDeviceDiscovery()).GetDevices().Select(d => new UsbEntry($"{d.VendorId:X4}:{d.ProductId:X4}", d.VendorId, d.ProductId, d.Product, d.SerialNumber))));

    private static List<T> Safe<T>(Func<IEnumerable<T>> discover)
    {
        try
        {
            return [.. discover()];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }
}
