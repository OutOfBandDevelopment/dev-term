using System.Collections.Generic;

namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// Describes one USB device whose active configuration exposes a USBTMC
/// (USB Test &amp; Measurement Class, interface class 0xFE / sub-class 0x03) interface.
/// </summary>
public sealed record UsbtmcDeviceDescriptor(
    int VendorId,
    int ProductId,
    string? Manufacturer,
    string? Product,
    string? SerialNumber);

/// <summary>
/// Enumerates USBTMC-class devices currently attached to the system.
/// </summary>
public interface IUsbtmcDeviceDiscovery
{
    /// <summary>
    /// Returns every attached USB device that exposes a USBTMC interface
    /// (interface class 0xFE, sub-class 0x03) in its active configuration.
    /// </summary>
    IReadOnlyList<UsbtmcDeviceDescriptor> GetDevices();
}
