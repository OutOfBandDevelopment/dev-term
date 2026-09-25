namespace DevTerm.Transports.Serial;

/// <summary>
/// The one description format Linux (sysfs) and macOS (IOKit) share, built from a USB device's
/// string descriptors: <c>"FTDI FT232R USB UART (0403:6001, serial A50285BI)"</c>. Windows uses the
/// Plug-and-Play friendly name as-is instead (see <see cref="WindowsSerialPortDescriptions"/>).
/// The serial number is kept because it's what tells two identical adapters apart in a picker.
/// </summary>
internal static class SerialPortDescriptionFormat
{
    /// <param name="manufacturer">USB manufacturer string, or null.</param>
    /// <param name="product">USB product string, or null.</param>
    /// <param name="serial">USB serial number string, or null.</param>
    /// <param name="ids">Lower-case <c>"vvvv:pppp"</c> vendor/product id, or null.</param>
    /// <returns>The description, or null when there's nothing at all to say.</returns>
    public static string? Format(string? manufacturer, string? product, string? serial, string? ids)
    {
        manufacturer = Clean(manufacturer);
        product = Clean(product);
        serial = Clean(serial);
        ids = Clean(ids);

        string? name;
        if (product is not null)
        {
            name = manufacturer is not null && !product.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
                ? $"{manufacturer} {product}"
                : product;
        }
        else
        {
            name = manufacturer;
        }

        var details = new List<string>(2);
        if (ids is not null)
        {
            details.Add(ids);
        }

        if (serial is not null)
        {
            details.Add($"serial {serial}");
        }

        if (name is null)
        {
            return ids is null ? null : $"USB device {string.Join(", ", details)}";
        }

        return details.Count == 0 ? name : $"{name} ({string.Join(", ", details)})";
    }

    public static string Ids(long vendorId, long productId) =>
        $"{vendorId & 0xFFFF:x4}:{productId & 0xFFFF:x4}";

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
