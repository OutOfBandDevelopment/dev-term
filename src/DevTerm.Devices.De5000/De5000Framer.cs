namespace DevTerm.Devices.De5000;

/// <summary>
/// One decoded DE-5000 measurement packet, per
/// docs/design/proposals/de5000-lcr-meter-protocol.md. All string fields are null when the source
/// byte's value has no defined meaning at that position (e.g. an out-of-range status nibble) rather
/// than an empty string, so a caller can tell "decoded, no meaning" apart from "not decoded".
/// </summary>
public readonly record struct De5000Frame(
    bool Hold,
    bool ReferenceShown,
    bool Delta,
    bool Calibration,
    bool Sorting,
    bool LcrAuto,
    bool AutoRange,
    bool Parallel,
    string? Frequency,
    string? Tolerance,
    string? PrimaryQuantity,
    double PrimaryValue,
    string? PrimaryUnit,
    string? PrimaryStatus,
    string? SecondaryQuantity,
    double SecondaryValue,
    string? SecondaryUnit,
    string? SecondaryStatus);

/// <summary>
/// Parses the DE-5000's (really the underlying Cyrustek ES51919 chipset's) fixed 17-byte packet, per
/// docs/design/proposals/de5000-lcr-meter-protocol.md. Field layout, lookup tables, and the value
/// calculation below are all verified against
/// <see href="https://github.com/4x1md/de5000_lcr_py">4x1md/de5000_lcr_py</see>'s actual parsing code
/// (<c>src/de5000.py</c>), not just its README prose — two real discrepancies existed between the two:
/// the footer is <c>0x0D 0x0A</c> (the README's byte-by-byte table; its own summary line says
/// <c>0x0D 0x0D</c>, which the code disproves), and each 16-bit measurement value is
/// <c>MSB * 0x100 + LSB</c> (the README's prose says <c>0x10000</c>, which would be wrong for a
/// 2-byte value).
/// </summary>
public static class De5000Framer
{
    public const int FrameLength = 17;

    private const byte _header0 = 0x00;
    private const byte _header1 = 0x0D;
    private const byte _footer0 = 0x0D;
    private const byte _footer1 = 0x0A;

    private const byte _flagHold = 0b0000_0001;
    private const byte _flagReferenceShown = 0b0000_0010;
    private const byte _flagDelta = 0b0000_0100;
    private const byte _flagCalibration = 0b0000_1000;
    private const byte _flagSorting = 0b0001_0000;
    private const byte _flagLcrAuto = 0b0010_0000;
    private const byte _flagAutoRange = 0b0100_0000;
    private const byte _flagParallel = 0b1000_0000;

    // Index = byte 5/10 quantity code. Which of the two byte-5 tables applies depends on the
    // Parallel flag (byte 2 bit 7) - the same physical quantity is labeled "series" or "parallel"
    // depending on the meter's equivalent-circuit mode, not two different measurements.
    private static readonly string?[] _seriesQuantities = [null, "Ls", "Cs", "Rs", "DCR"];
    private static readonly string?[] _parallelQuantities = [null, "Lp", "Cp", "Rp", "DCR"];
    private static readonly string?[] _secondaryQuantities = [null, "D", "Q", "ESR", "Theta"];

    // Index = units nibble (byte 8/13 bits 3-7). Index 0 is genuinely the empty string in the
    // source table (a dimensionless/unset reading), not a gap - only index 4 is a real gap (None).
    private static readonly string?[] _units =
        ["", "Ohm", "kOhm", "MOhm", null, "uH", "mH", "H", "kH", "pF", "nF", "uF", "mF", "%", "deg"];

    // Index = frequency code (byte 3 bits 5-7, values 0-5 only - 6/7 are unused encodings).
    private static readonly string?[] _frequencies = ["100 Hz", "120 Hz", "1 kHz", "10 kHz", "100 kHz", "DC"];

    // Index = raw byte 4 (sorting-mode tolerance), no mask/shift - values 0-2 have no defined tolerance.
    private static readonly string?[] _tolerances =
        [null, null, null, "+-0.25%", "+-0.5%", "+-1%", "+-2%", "+-5%", "+-10%", "+-20%", "-20%/+80%"];

    // Index = status nibble. Primary (byte 9) uses the low 4 bits (values 0-10 all reachable);
    // secondary (byte 14) uses only the low 3 bits, so values 8-10 (FAIL/OPEn/Srt) never occur there
    // - a real asymmetry in the source, not an oversight to "fix" into matching masks.
    private static readonly string?[] _status =
        ["normal", "blank", "----", "OL", null, null, null, "PASS", "FAIL", "OPEn", "Srt"];

    /// <summary>
    /// Parses exactly <see cref="FrameLength"/> bytes starting at <paramref name="frame"/>'s own
    /// start as one measurement packet. Returns false for a wrong-length span, a wrong header, or a
    /// wrong footer - a caller scanning a byte stream for frame boundaries treats any of those as
    /// "not a frame at this offset", not a parse error.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out De5000Frame parsed)
    {
        parsed = default;

        if (frame.Length != FrameLength
            || frame[0] != _header0 || frame[1] != _header1
            || frame[15] != _footer0 || frame[16] != _footer1)
        {
            return false;
        }

        var flags = frame[2];
        var isParallel = (flags & _flagParallel) != 0;

        var primaryQuantityCode = frame[5];
        var primaryQuantity = LookUp(isParallel ? _parallelQuantities : _seriesQuantities, primaryQuantityCode);
        var primaryValue = (frame[6] * 0x100 + frame[7]) * Math.Pow(10, -(frame[8] & 0b0000_0111));
        var primaryUnit = LookUp(_units, (byte)((frame[8] & 0b1111_1000) >> 3));
        var primaryStatus = LookUp(_status, (byte)(frame[9] & 0b0000_1111));

        var secondaryQuantityCode = frame[10];
        var secondaryQuantity = LookUp(_secondaryQuantities, secondaryQuantityCode);
        var secondaryValue = (frame[11] * 0x100 + frame[12]) * Math.Pow(10, -(frame[13] & 0b0000_0111));
        var secondaryUnit = LookUp(_units, (byte)((frame[13] & 0b1111_1000) >> 3));
        var secondaryStatus = LookUp(_status, (byte)(frame[14] & 0b0000_0111));

        parsed = new De5000Frame(
            Hold: (flags & _flagHold) != 0,
            ReferenceShown: (flags & _flagReferenceShown) != 0,
            Delta: (flags & _flagDelta) != 0,
            Calibration: (flags & _flagCalibration) != 0,
            Sorting: (flags & _flagSorting) != 0,
            LcrAuto: (flags & _flagLcrAuto) != 0,
            AutoRange: (flags & _flagAutoRange) != 0,
            Parallel: isParallel,
            Frequency: LookUp(_frequencies, (byte)((frame[3] & 0b1110_0000) >> 5)),
            Tolerance: LookUp(_tolerances, frame[4]),
            PrimaryQuantity: primaryQuantity,
            PrimaryValue: primaryValue,
            PrimaryUnit: primaryUnit,
            PrimaryStatus: primaryStatus,
            SecondaryQuantity: secondaryQuantity,
            SecondaryValue: secondaryValue,
            SecondaryUnit: secondaryUnit,
            SecondaryStatus: secondaryStatus);
        return true;
    }

    private static string? LookUp(string?[] table, byte index) => index < table.Length ? table[index] : null;
}
