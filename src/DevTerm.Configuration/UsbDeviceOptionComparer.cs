namespace DevTerm.Configuration;

public sealed class UsbDeviceOptionComparer : IComparer<IUsbDeviceOption>
{
    private static readonly UsbDeviceOptionComparer _instance = new();
    public static UsbDeviceOptionComparer Default => _instance;

    public int Compare(IUsbDeviceOption? x, IUsbDeviceOption? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var cmp = x.VendorId.CompareTo(y.VendorId);
        if (cmp != 0) return cmp;

        cmp = x.ProductId.CompareTo(y.ProductId);
        if (cmp != 0) return cmp;

        cmp= CompareSerial(x.SerialNumber, y.SerialNumber);
        if (cmp != 0) return cmp;

        return CompareSerial(x.DevicePath, y.DevicePath);
    }

    private static int CompareSerial(string? x, string? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return string.Compare(x, y, StringComparison.Ordinal);
    }
}
