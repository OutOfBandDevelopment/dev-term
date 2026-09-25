using System.Collections.Generic;
using System.Linq;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;

namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// Enumerates USBTMC-class devices via libusb (<see cref="UsbContext"/>).
/// </summary>
public sealed class SystemUsbtmcDeviceDiscovery : IUsbtmcDeviceDiscovery
{
    private const byte _usbtmcInterfaceSubClass = 0x03;

    public IReadOnlyList<UsbtmcDeviceDescriptor> GetDevices()
    {
        var descriptors = new List<UsbtmcDeviceDescriptor>();

        using var context = new UsbContext();
        foreach (var device in context.List())
        {
            // IUsbDevice (LibUsbDotNet.Device) is a SafeHandle wrapping a native libusb device
            // reference - every device context.List() hands back must be disposed before this
            // method returns, or its finalizer runs on the GC finalizer thread later, potentially
            // after `context` above has already been disposed/freed. Unref'ing a device against an
            // already-destroyed libusb context is undefined behavior; confirmed against real
            // hardware as the cause of a native access-violation crash in
            // LibUsbDotNet.NativeMethods.UnrefDevice during test-process teardown.
            try
            {
                var isUsbtmc = device.Configs.Any(config => config.Interfaces.Any(
                    iface => iface.Class == ClassCode.Application && iface.SubClass == _usbtmcInterfaceSubClass));

                if (!isUsbtmc)
                {
                    continue;
                }

                // The string descriptors (Manufacturer/Product/SerialNumber) need an actual control
                // transfer, which needs an open device handle — VendorId/ProductId alone come from the
                // already-enumerated device descriptor and need no Open(). Opening can fail (e.g. no
                // WinUSB-compatible driver bound to the device on Windows) independent of whether the
                // device itself is a valid USBTMC device, so a failed Open still yields a descriptor,
                // just without the string fields.
                var opened = TryOpen(device);
                try
                {
                    var info = device.Info;
                    descriptors.Add(new UsbtmcDeviceDescriptor(
                        info.VendorId,
                        info.ProductId,
                        TryGet(() => info.Manufacturer),
                        TryGet(() => info.Product),
                        TryGet(() => info.SerialNumber)));
                }
                finally
                {
                    if (opened)
                    {
                        try
                        {
                            device.Close();
                        }
                        catch
                        {
                            // Best-effort cleanup only; a failed Close doesn't change what was detected.
                        }
                    }
                }
            }
            finally
            {
                device.Dispose();
            }
        }

        return descriptors;
    }

    private static bool TryOpen(IUsbDevice device)
    {
        try
        {
            device.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGet(System.Func<string?> accessor)
    {
        try
        {
            // LibUsbDotNet's string-descriptor properties come back padded with trailing NUL
            // characters from the underlying fixed-size descriptor buffer - confirmed against real
            // hardware (a Rigol DS1102E's serial number read back as "DS1ET180300759\0"), which
            // otherwise shows up as literal control characters in --listusbtmcdevices output and
            // breaks exact-match comparisons elsewhere (see SystemUsbtmcDevice.TryGetSerialNumber).
            return accessor()?.TrimEnd('\0');
        }
        catch
        {
            return null;
        }
    }
}
