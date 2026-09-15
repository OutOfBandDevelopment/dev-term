using System.ComponentModel.DataAnnotations;

namespace DevTerm.Transports.Hid;

/// <summary>Configuration for a USB HID device session. Bound via the Options pattern.</summary>
public sealed class HidTransportOptions
{
    /// <summary>
    /// USB Vendor ID, decimal. Windows Device Manager shows this in hex (e.g. "VID_1915" is 6421
    /// decimal) — convert before setting this.
    /// </summary>
    [Range(1, 0xFFFF)]
    public int VendorId { get; set; }

    /// <summary>
    /// USB Product ID, decimal. Windows Device Manager shows this in hex (e.g. "PID_AFDA" is
    /// 45018 decimal) — convert before setting this.
    /// </summary>
    [Range(1, 0xFFFF)]
    public int ProductId { get; set; }

    /// <summary>Disambiguates when more than one connected device matches VendorId/ProductId. Optional.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Milliseconds a read blocks before timing out. Unlike serial, HID has no event to wait on
    /// for "a report is ready" — <see cref="SystemHidDevice"/>'s read-loop thread has to block on
    /// a real read and this timeout is how it periodically notices it should stop on close,
    /// rather than relying on cancelling an in-flight native read directly (that's not reliable
    /// for <see cref="System.IO.Ports.SerialPort"/> either — see
    /// <c>DevTerm.Transports.Serial.SerialPortReadStream</c> — and hasn't been verified one way or
    /// the other for HidSharp against real hardware, so the same conservative approach is used
    /// here without assuming it's unnecessary).
    /// </summary>
    public int ReadTimeoutMs { get; set; } = 1000;

    /// <summary>Bounds a blocked write so it fails with a <see cref="TimeoutException"/> instead of hanging forever.</summary>
    public int WriteTimeoutMs { get; set; } = 5000;
}
