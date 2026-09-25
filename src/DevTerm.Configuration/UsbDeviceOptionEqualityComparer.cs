namespace DevTerm.Configuration;

public sealed class UsbDeviceOptionEqualityComparer<T> : IEqualityComparer<T> where T : IUsbDeviceOption
{
    private static readonly UsbDeviceOptionEqualityComparer<T> _instance = new();
    public static UsbDeviceOptionEqualityComparer<T> Default => _instance;

    public bool Equals(T? x, T? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return x.VendorId == y.VendorId
            && x.ProductId == y.ProductId
            && string.Equals(x.SerialNumber, y.SerialNumber, StringComparison.Ordinal);
    }

    public int GetHashCode(T obj) =>
        HashCode.Combine(obj.VendorId, obj.ProductId, obj.SerialNumber);
}
