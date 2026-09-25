using System.Diagnostics;
using LibUsbDotNet.LibUsb;

namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// A USBTMC device's <c>DevicePath</c>: its physical position on the USB tree - the bus number
/// and the hub-port chain from the root - formatted like Linux sysfs names it, e.g.
/// <c>usb:1-4.2</c> (bus 1, root port 4, then port 2 of the hub on it). Unlike the device address
/// it survives unplugging and replugging into the same port, and unlike the serial number it can
/// tell two identical instruments apart even when their firmware reports no (or the same) serial.
/// The USBTMC counterpart of a HID device's OS device path.
/// </summary>
public static class UsbtmcDeviceLocation
{
    public const string Prefix = "usb:";

    /// <summary>Null when the port chain is empty (a root hub, or a backend that can't report it).</summary>
    /// <summary>The location of a libusb-enumerated device, or null if the backend can't report it.</summary>
    internal static string? TryGet(IUsbDevice device)
    {
        try
        {
            return device is UsbDevice usbDevice ? Format(usbDevice.BusNumber, usbDevice.PortNumbers) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"USBTMC: could not read a device's USB location: {ex}");
            return null;
        }
    }

    public static string? Format(int busNumber, IEnumerable<byte> portNumbers)
    {
        ArgumentNullException.ThrowIfNull(portNumbers);
        var ports = string.Join('.', portNumbers);
        return ports.Length == 0 ? null : $"{Prefix}{busNumber}-{ports}";
    }
}
