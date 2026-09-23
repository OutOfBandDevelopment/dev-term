using System.Globalization;
using DevTerm.Transports.Usbtmc;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.UsbtmcDeviceOptions"/> — a real,
/// currently-connected USBTMC device formatted for display (matching <c>--listusbtmcdevices</c>'s
/// own <c>"{VID:X4}:{PID:X4}  {Product}"</c> shape) alongside the plain decimal
/// <see cref="VendorId"/>/<see cref="ProductId"/> that actually get written into
/// <see cref="ConnectionEditorViewModel.VendorId"/>/<see cref="ConnectionEditorViewModel.ProductId"/>
/// (shared with the HID transport's own picker) when picked.
/// </summary>
public sealed record UsbtmcDeviceOption(string Display, int VendorId, int ProductId, string? SerialNumber)
{
    public static UsbtmcDeviceOption FromDescriptor(UsbtmcDeviceDescriptor descriptor)
    {
        var id = string.Format(CultureInfo.InvariantCulture, "{0:X4}:{1:X4}", descriptor.VendorId, descriptor.ProductId);
        var display = string.IsNullOrEmpty(descriptor.Product) ? id : $"{id}  {descriptor.Product}";
        return new UsbtmcDeviceOption(display, descriptor.VendorId, descriptor.ProductId, descriptor.SerialNumber);
    }
}
