using System.Globalization;
using DevTerm.Transports.Hid;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.HidDeviceOptions"/> — a real, currently-connected
/// HID device formatted for display (matching <c>--listhiddevices</c>'s own
/// <c>"{VID:X4}:{PID:X4}  {ProductName}"</c> shape) alongside the plain decimal
/// <see cref="VendorId"/>/<see cref="ProductId"/> that actually get written into
/// <see cref="ConnectionEditorViewModel.VendorId"/>/<see cref="ConnectionEditorViewModel.ProductId"/>
/// (shared with the USBTMC transport's own picker) when picked. <see cref="SerialNumber"/> falls back
/// to the device's <see cref="HidDeviceDescriptor.DevicePath"/> (see <see cref="FromDescriptor"/>) when
/// the device has no real serial descriptor — confirmed live: three simultaneously-attached Velleman
/// K8055 boards, same board address, so same VID/PID and no serial. Folding the fallback into this one
/// field (rather than keeping <c>DevicePath</c> as a separate property here) means the connection
/// profile file and the UI's serial-number field stay a single value, and a lookup (e.g. best-matching
/// a loaded profile back to a live device) only ever has one field to check, not two. <see
/// cref="DevicePath"/> is still carried through separately, purely to build a short, human-readable
/// disambiguating suffix for <see cref="Display"/> when two devices would otherwise show identically
/// (see <c>ConnectionEditorViewModel.DisambiguateDisplay</c>) — it does not participate in equality
/// beyond that, since <see cref="SerialNumber"/> already disambiguates the record itself once the
/// fallback applies.
/// </summary>
public sealed record HidDeviceOption(string Display, int VendorId, int ProductId, string? SerialNumber, string DevicePath)
{
    public static HidDeviceOption FromDescriptor(HidDeviceDescriptor descriptor)
    {
        var id = string.Format(CultureInfo.InvariantCulture, "{0:X4}:{1:X4}", descriptor.VendorId, descriptor.ProductId);
        var display = string.IsNullOrEmpty(descriptor.ProductName) ? id : $"{id}  {descriptor.ProductName}";
        var serialNumber = string.IsNullOrWhiteSpace(descriptor.SerialNumber) ? descriptor.DevicePath : descriptor.SerialNumber;
        return new HidDeviceOption(display, descriptor.VendorId, descriptor.ProductId, serialNumber, descriptor.DevicePath);
    }
}
