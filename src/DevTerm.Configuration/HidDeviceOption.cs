using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DevTerm.Transports.Hid;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.HidDeviceOptions"/> — a real, currently-connected
/// HID device formatted for display (matching <c>--listhiddevices</c>'s own
/// <c>"{VID:X4}:{PID:X4}  {ProductName}"</c> shape) alongside the plain decimal
/// <see cref="VendorId"/>/<see cref="ProductId"/> that actually get written into
/// <see cref="ConnectionEditorViewModel.VendorId"/>/<see cref="ConnectionEditorViewModel.ProductId"/>
/// (shared with the USBTMC transport's own picker) when picked. <see cref="SerialNumber"/> is the
/// device's real serial descriptor, untouched — <see langword="null"/>/empty when it has none
/// (confirmed live: three simultaneously-attached Velleman K8055 boards, same board address, so same
/// VID/PID and no serial descriptor at all). <see cref="DevicePath"/> is always populated (every
/// enumerated device has one, unlike <see cref="SerialNumber"/>) and is kept as its own field —
/// <em>not</em> folded into <see cref="SerialNumber"/> — because it means something different: it's
/// the OS's per-connection instance path, tied to a physical USB hub/port, whereas a real serial number
/// is portable across ports. Both are saved into a profile and both matched on when reopening/reselecting
/// a device (<see cref="ConnectionEditorViewModel"/>'s best-match logic and
/// <c>DevTerm.Transports.Hid.SystemHidDevice.Open</c> prefer an exact <see cref="SerialNumber"/> match
/// when it's non-blank, falling back to <see cref="DevicePath"/> otherwise) — the least-surprising
/// choice for a serial-less device like the K8055, at the accepted cost that moving it to a different
/// USB hub/port makes it look unmatched.
/// </summary>
public sealed record HidDeviceOption(string Display, int VendorId, int ProductId, string? SerialNumber, string DevicePath) : IUsbDeviceOption
{
    public static HidDeviceOption FromDescriptor(HidDeviceDescriptor descriptor)
    {
        var id = string.Format(CultureInfo.InvariantCulture, "{0:X4}:{1:X4}", descriptor.VendorId, descriptor.ProductId);
        var display = string.IsNullOrEmpty(descriptor.ProductName) ? id : $"{id}  {descriptor.ProductName}";
        var serialNumber = string.IsNullOrWhiteSpace(descriptor.SerialNumber) ? null : descriptor.SerialNumber;
        return new HidDeviceOption(display, descriptor.VendorId, descriptor.ProductId, serialNumber, descriptor.DevicePath);
    }
}
