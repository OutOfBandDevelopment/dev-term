namespace DevTerm.Configuration;

/// <summary>The device control panels a main window's Device menu offers.</summary>
public enum DevicePanel
{
    K8055,
    Busylight,
    Scpi,
    RadexOne,
}

/// <summary>
/// Whether a Device menu panel makes sense for the current connection - shared by the TUI's and
/// WPF's Device menus, which disable an item that can't work (opening the K8055 panel on a TCP
/// oscilloscope connection, say, or any panel while disconnected) instead of letting it open a
/// panel whose every command would fail.
/// </summary>
public static class DevicePanels
{
    // Velleman K8055/VM110: vendor 0x10CF, one product id per board address jumper setting (0-3).
    private const int _k8055VendorId = 0x10CF;
    private const int _k8055FirstProductId = 0x5500;
    private const int _k8055LastProductId = 0x5503;

    // Kuando Busylight: the bench unit (0x04D8:0xF848, Microchip's vendor id - real-hardware
    // confirmed) plus Plenom's own vendor id, which newer Busylight models enumerate under.
    private const int _busylightMicrochipVendorId = 0x04D8;
    private const int _busylightMicrochipProductId = 0xF848;
    private const int _plenomVendorId = 0x27BB;

    public static bool IsAvailable(DevicePanel panel, CliOptions options, bool connected)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!connected)
        {
            return false;
        }

        var isHid = string.Equals(options.Transport, "hid", StringComparison.OrdinalIgnoreCase);
        return panel switch
        {
            DevicePanel.K8055 => isHid
                && options.VendorId == _k8055VendorId
                && options.ProductId is >= _k8055FirstProductId and <= _k8055LastProductId,
            DevicePanel.Busylight => isHid
                && ((options.VendorId == _busylightMicrochipVendorId && options.ProductId == _busylightMicrochipProductId)
                    || options.VendorId == _plenomVendorId),

            // SCPI is text over a byte stream: any transport but HID (fixed-size binary reports).
            DevicePanel.Scpi => !isHid,

            // Radex One's real VID/PID is unconfirmed (device wasn't enumerated during development -
            // see RadexOneHidFraming's doc comment) - gated on "any HID connection" rather than a
            // specific id pair until a real device confirms one. Flagged for tightening later.
            DevicePanel.RadexOne => isHid,
            _ => false,
        };
    }
}
