namespace DevTerm.Devices.RadexOne;

/// <summary>
/// Wraps/unwraps a <see cref="RadexOneFramer"/> packet inside a USB HID report.
///
/// <para><b>This is a best-effort guess, not a confirmed framing</b> — the source reverse-engineering
/// writeup this proposal is drawn from documented the packet bytes above assuming a virtual COM port;
/// the device actually enumerates as USB HID (see docs/design/proposals/radex-one-protocol.md's
/// "Device" section), and how the packet is carried inside a HID report (report ID, fixed report
/// length) was never captured. Two things are assumed here, both flagged for real-hardware
/// verification (see <c>BACKLOG.md</c>'s "Device control modules &amp; hardware profiles" section and
/// <c>tests/DevTerm.Devices.RadexOne.Tests/RealHardwareRadexOneTests.cs</c>):</para>
/// <list type="bullet">
/// <item>A single leading HID report-ID byte, <c>0x00</c>, ahead of the packet — the same convention
/// already confirmed live for the Velleman K8055 and Kuando Busylight (see
/// <c>DevTerm.Devices.K8055.K8055ControlSurface</c>'s and
/// <c>DevTerm.Devices.Busylight.BusylightControlSurface</c>'s own doc comments): Windows'
/// <c>HidD_SetOutputReport</c> requires the report ID as the buffer's first byte even for a device
/// with no report IDs of its own.</item>
/// <item>A fixed 64-byte report body (<see cref="AssumedReportBodyLength"/>) — the common size for a
/// simple microcontroller-based USB HID device, and comfortably large enough for every known Radex One
/// payload (the biggest, the Read Serial/Version reply's ASCII string, is under 30 bytes). Only needed
/// for <em>writing</em> a request: Windows HID output reports are a fixed, device-defined length (see
/// <c>HidTransport.WriteAsync</c>'s own doc comment on this), so a request shorter than the device's
/// real report size needs zero-padding to match. Reading doesn't depend on this guess being right —
/// <see cref="RadexOneFramer.TryParseReply"/> trusts the packet's own declared
/// <c>ExtensionLength</c> field, not the report's total length, so trailing padding (correct or not)
/// is simply ignored.</item>
/// </list>
/// </summary>
public static class RadexOneHidFraming
{
    private const byte _reportId = 0x00;

    /// <summary>Best-effort guess at the device's output report body length (excluding the report-ID byte) — see the type's own doc comment.</summary>
    public const int AssumedReportBodyLength = 64;

    /// <summary>Wraps a <see cref="RadexOneFramer"/> request packet as a full HID report: report-ID byte + the packet, zero-padded to <see cref="AssumedReportBodyLength"/>.</summary>
    public static byte[] WrapRequest(byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Length > AssumedReportBodyLength)
        {
            throw new ArgumentException(
                $"Packet is {packet.Length} bytes, longer than the assumed {AssumedReportBodyLength}-byte HID report body — the report-length guess in {nameof(RadexOneHidFraming)} needs revisiting against real hardware.",
                nameof(packet));
        }

        var report = new byte[1 + AssumedReportBodyLength];
        report[0] = _reportId;
        packet.CopyTo(report, 1);
        return report;
    }

    /// <summary>Strips the assumed leading report-ID byte off a received HID report, leaving the framer packet (still possibly zero-padded — see <see cref="RadexOneFramer.TryParseReply"/>).</summary>
    public static ReadOnlySpan<byte> UnwrapReply(ReadOnlySpan<byte> report) => report.IsEmpty ? report : report[1..];
}
