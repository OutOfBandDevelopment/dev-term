namespace DevTerm.Configuration;

public interface IUsbDeviceOption
{
    int VendorId { get; }
    int ProductId { get; }
    string? SerialNumber { get; }
    string? DevicePath { get; }
}
