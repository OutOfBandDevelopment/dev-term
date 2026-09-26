using DevTerm.Test.Utilities;

namespace DevTerm.Devices.Nmea.Tests;

/// <summary>
/// Exercises <see cref="NmeaSentence"/>'s parsing/checksum/field-conversion helpers directly against
/// literal NMEA 0183 sentences with real, independently-computed checksums (the well-known GGA/RMC/
/// GSA/GSV/VTG examples from the NMEA reference sentences most GPS documentation reprints) — no
/// device or transport involved, so this is <c>Unit</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class NmeaSentenceTests
{
    [TestMethod]
    public void TryParse_ValidGgaWithChecksum_ReturnsTypeFieldsAndNoMismatch()
    {
        var ok = NmeaSentence.TryParse(
            "$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*47",
            out var type, out var fields, out var mismatch);

        Assert.IsTrue(ok);
        Assert.AreEqual("GGA", type);
        Assert.IsFalse(mismatch);
        Assert.AreEqual("123519", fields[0]);
        Assert.AreEqual("4807.038", fields[1]);
        Assert.AreEqual("N", fields[2]);
    }

    [TestMethod]
    public void TryParse_CorruptedChecksum_StillParsesButReportsMismatch()
    {
        var ok = NmeaSentence.TryParse(
            "$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*00",
            out var type, out _, out var mismatch);

        Assert.IsTrue(ok);
        Assert.AreEqual("GGA", type);
        Assert.IsTrue(mismatch);
    }

    [TestMethod]
    public void TryParse_NoChecksumPresent_ParsesWithoutMismatch()
    {
        var ok = NmeaSentence.TryParse("$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W", out var type, out var fields, out var mismatch);

        Assert.IsTrue(ok);
        Assert.AreEqual("RMC", type);
        Assert.IsFalse(mismatch);
        Assert.AreEqual(11, fields.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not nmea")]
    [DataRow("$GP")]
    public void TryParse_NotShapedLikeASentence_ReturnsFalse(string rawLine)
    {
        var ok = NmeaSentence.TryParse(rawLine, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ToDecimalDegrees_NorthAndEast_AreBothPositive()
    {
        var lat = NmeaSentence.ToDecimalDegrees("4807.038", "N");
        var lon = NmeaSentence.ToDecimalDegrees("01131.000", "E");

        Assert.IsNotNull(lat);
        Assert.IsNotNull(lon);
        Assert.AreEqual(48.1173, lat!.Value, 0.0001);
        Assert.AreEqual(11.5167, lon!.Value, 0.0001);
    }

    [TestMethod]
    public void ToDecimalDegrees_SouthAndWest_AreBothNegative()
    {
        var lat = NmeaSentence.ToDecimalDegrees("4807.038", "S");
        var lon = NmeaSentence.ToDecimalDegrees("01131.000", "W");

        Assert.AreEqual(-48.1173, lat!.Value, 0.0001);
        Assert.AreEqual(-11.5167, lon!.Value, 0.0001);
    }

    [TestMethod]
    public void ToDecimalDegrees_BlankOrUnparseable_ReturnsNull()
    {
        Assert.IsNull(NmeaSentence.ToDecimalDegrees(string.Empty, "N"));
        Assert.IsNull(NmeaSentence.ToDecimalDegrees("not-a-number", "N"));
    }

    [TestMethod]
    public void FormatUtcTime_SixDigitValue_FormatsAsHhMmSs()
    {
        Assert.AreEqual("12:35:19 UTC", NmeaSentence.FormatUtcTime("123519"));
    }

    [TestMethod]
    public void FormatUtcTime_WithFractionalSeconds_KeepsOnlyWholeSeconds()
    {
        Assert.AreEqual("12:35:19 UTC", NmeaSentence.FormatUtcTime("123519.533"));
    }

    [TestMethod]
    public void FormatUtcTime_TooShort_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, NmeaSentence.FormatUtcTime("123"));
    }

    [TestMethod]
    public void FormatUtcDate_SixDigitValue_FormatsAs21stCenturyIsoDate()
    {
        // ddmmyy -> 20yy-mm-dd, always the 21st century regardless of the real date - see
        // NmeaSentence.FormatUtcDate's doc comment.
        Assert.AreEqual("2094-03-23", NmeaSentence.FormatUtcDate("230394"));
    }

    [TestMethod]
    public void FormatUtcDate_WrongLength_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, NmeaSentence.FormatUtcDate("2303"));
    }
}
