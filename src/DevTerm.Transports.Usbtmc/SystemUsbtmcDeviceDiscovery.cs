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
    private const byte UsbtmcInterfaceSubClass = 0x03;

    public IReadOnlyList<UsbtmcDeviceDescriptor> GetDevices()
    {
        var descriptors = new List<UsbtmcDeviceDescriptor>();

        using var context = new UsbContext();
        foreach (var device in context.List())
        {
            var isUsbtmc = device.Configs.Any(config => config.Interfaces.Any(
                iface => iface.Class == ClassCode.Application && iface.SubClass == UsbtmcInterfaceSubClass));

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
            return accessor();
        }
        catch
        {
            return null;
        }
    }
}
