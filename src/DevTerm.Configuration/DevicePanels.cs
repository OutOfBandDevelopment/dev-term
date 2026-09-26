namespace DevTerm.Configuration;

/// <summary>The device control panels a main window's Device menu offers.</summary>
public enum DevicePanel
{
    K8055,
    Busylight,
    Scpi,
    RadexOne,
    ZoomH4n,
    De5000,

    /// <summary>A panel built from a loaded device manifest (Device > Device Manifest...) — any connection.</summary>
    Manifest,
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

            // Radex One is a plain virtual-COM-port device (2400 8N1, real-hardware confirmed
            // 2026-09-25 on COM8 - it does not enumerate as HID at all, contradicting this module's
            // original "confirmed directly" HID assumption) - gated on "any serial connection", like
            // Zoom H4n's.
            DevicePanel.RadexOne => string.Equals(options.Transport, "serial", StringComparison.OrdinalIgnoreCase),

            // Zoom H4n's RC04/RC2 remote port is presented as plain serial (via the h4n2rs485
            // adapter, see docs/design/proposals/zoom-h4n-remote-protocol.md) - no VID/PID to gate
            // on, so any serial connection is offered, like SCPI's "any non-HID transport".
            DevicePanel.ZoomH4n => string.Equals(options.Transport, "serial", StringComparison.OrdinalIgnoreCase),

            // The DE-5000's optical-to-BLE adapter has no VID/PID (it's a GATT peripheral, not a
            // USB device) - gated on "any BLE connection", like ZoomH4n's "any serial connection".
            DevicePanel.De5000 => string.Equals(options.Transport, "ble", StringComparison.OrdinalIgnoreCase),

            // A manifest names its own transport and commands, so which manifests make sense is the
            // user's call (the picker) - the item is enabled whenever connected.
            DevicePanel.Manifest => true,
            _ => false,
        };
    }
}
