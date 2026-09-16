using System.Globalization;
using DevTerm.Transports.Hid;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.HidDeviceOptions"/> — a real, currently-connected
/// HID device formatted for display (matching <c>--listhiddevices</c>'s own
/// <c>"{VID:X4}:{PID:X4}  {ProductName}"</c> shape) alongside the plain decimal
/// <see cref="VendorId"/>/<see cref="ProductId"/> that actually get written into
/// <see cref="ConnectionEditorViewModel.HidVendorId"/>/<see cref="ConnectionEditorViewModel.HidProductId"/>
/// when picked.
/// </summary>
public sealed record HidDeviceOption(string Display, int VendorId, int ProductId)
{
    public static HidDeviceOption FromDescriptor(HidDeviceDescriptor descriptor)
    {
        var id = string.Format(CultureInfo.InvariantCulture, "{0:X4}:{1:X4}", descriptor.VendorId, descriptor.ProductId);
        var display = string.IsNullOrEmpty(descriptor.ProductName) ? id : $"{id}  {descriptor.ProductName}";
        return new HidDeviceOption(display, descriptor.VendorId, descriptor.ProductId);
    }
}
