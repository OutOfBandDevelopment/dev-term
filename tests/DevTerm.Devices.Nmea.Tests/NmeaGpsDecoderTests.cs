using System.Buffers;
using System.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Devices.Nmea.Tests;

/// <summary>
/// Feeds <see cref="NmeaGpsDecoder"/> synthetic NMEA 0183 sentence bytes (real, independently-
/// computed checksums) and asserts both the rendered summary text and the structured values it
/// publishes — no device or transport involved, so this is <c>Unit</c>. The embedded-NUL-byte test
/// below exercises the decoder's defensive HID-framing assumption described in its own remarks
/// (unverified against real hardware — see docs/design/proposals/nmea-gps-protocol.md).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Delorme_EarthmateBt20)]
[TestClass]
public sealed class NmeaGpsDecoderTests
{
    private const string _validGga = "$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*47\r\n";
    private const string _validRmc = "$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A\r\n";
    private const string _validGsa = "$GPGSA,A,3,04,05,,09,12,,,24,,,,,2.5,1.3,2.1*39\r\n";
    private const string _validGsv = "$GPGSV,3,1,11,03,03,111,00,04,15,270,00,06,01,010,00,13,06,292,00*74\r\n";
    private const string _validVtg = "$GPVTG,054.7,T,034.4,M,005.5,N,010.2,K*48\r\n";

    private static ReadOnlySequence<byte> Bytes(string text) => new(Encoding.ASCII.GetBytes(text));

    [TestMethod]
    public void Render_WithEmptySequence_ProducesNoLines()
    {
        var decoder = new NmeaGpsDecoder();

        var lines = decoder.Render(ReadOnlySequence<byte>.Empty);

        Assert.IsEmpty(lines);
    }

    [TestMethod]
    public void Render_OneCompleteGgaLine_ProducesOneSummaryLine()
    {
        var decoder = new NmeaGpsDecoder();

        var lines = decoder.Render(Bytes(_validGga));

        Assert.HasCount(1, lines);
        Assert.AreEqual("GGA: fix=GPS fix sats=08 lat=48.117300 lon=11.516667 alt=545.4m hdop=0.9 time=12:35:19 UTC", lines[0]);
    }

    [TestMethod]
    public void Render_LineSplitAcrossTwoCalls_OnlyProducesALineOnceComplete()
    {
        var decoder = new NmeaGpsDecoder();
        var splitAt = _validGga.Length / 2;

        var first = decoder.Render(Bytes(_validGga[..splitAt]));
        Assert.IsEmpty(first);

        var second = decoder.Render(Bytes(_validGga[splitAt..]));
        Assert.HasCount(1, second);
    }

    [TestMethod]
    public void Render_TwoSentencesInOneSequence_ProducesTwoLines()
    {
        var decoder = new NmeaGpsDecoder();

        var lines = decoder.Render(Bytes(_validGga + _validRmc));

        Assert.HasCount(2, lines);
        StringAssert.StartsWith(lines[0], "GGA:");
        StringAssert.StartsWith(lines[1], "RMC:");
    }

    [TestMethod]
    public void Render_EmbeddedNulBytes_AreStrippedBeforeLineBuffering()
    {
        var decoder = new NmeaGpsDecoder();
        var withNuls = new List<byte> { 0x00, 0x00 };
        withNuls.AddRange(Encoding.ASCII.GetBytes(_validGga));
        withNuls.Add(0x00);

        var lines = decoder.Render(new ReadOnlySequence<byte>(withNuls.ToArray()));

        Assert.HasCount(1, lines);
        StringAssert.StartsWith(lines[0], "GGA:");
    }

    [TestMethod]
    public void Render_BadChecksum_AppendsMismatchWarning()
    {
        var decoder = new NmeaGpsDecoder();

        var lines = decoder.Render(Bytes("$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*00\r\n"));

        Assert.HasCount(1, lines);
        StringAssert.EndsWith(lines[0], "[checksum mismatch]");
    }

    [TestMethod]
    public void Render_UnrecognizedSentenceType_ReturnsRawTypeAndFields()
    {
        var decoder = new NmeaGpsDecoder();

        var lines = decoder.Render(Bytes("$GPZZZ,1,2,3*1F\r\n"));

        Assert.HasCount(1, lines);
        Assert.AreEqual("ZZZ: 1,2,3 [checksum mismatch]", lines[0]);
    }

    [TestMethod]
    public void Render_Rmc_PublishesPositionSpeedAndDate()
    {
        var decoder = new NmeaGpsDecoder();
        IReadOnlyDictionary<string, string>? published = null;
        decoder.ValuesChanged += (_, values) => published = values;

        decoder.Render(Bytes(_validRmc));

        Assert.IsNotNull(published);
        Assert.AreEqual("Active", published!["navStatus"]);
        Assert.AreEqual("022.4", published["speedKnots"]);
        Assert.AreEqual("084.4", published["courseTrue"]);

        // FormatUtcDate assumes the 21st century (ddmmyy -> 20yy-mm-dd) - see NmeaSentence's doc
        // comment. "230394" is a 20th-century date in the real-world example this sentence is drawn
        // from, so the decoded value is intentionally "wrong" by this known, documented assumption.
        Assert.AreEqual("2094-03-23", published["utcDate"]);
    }

    [TestMethod]
    public void Render_Gsa_PublishesFixTypeAndCountsUsedSatellites()
    {
        var decoder = new NmeaGpsDecoder();
        IReadOnlyDictionary<string, string>? published = null;
        decoder.ValuesChanged += (_, values) => published = values;

        decoder.Render(Bytes(_validGsa));

        Assert.IsNotNull(published);
        Assert.AreEqual("3D", published!["fixType"]);
        Assert.AreEqual("5", published["satellitesUsed"]);
        Assert.AreEqual("2.5", published["pdop"]);
        Assert.AreEqual("1.3", published["hdop"]);
        Assert.AreEqual("2.1", published["vdop"]);
    }

    [TestMethod]
    public void Render_Gsv_PublishesSatellitesInView()
    {
        var decoder = new NmeaGpsDecoder();
        IReadOnlyDictionary<string, string>? published = null;
        decoder.ValuesChanged += (_, values) => published = values;

        decoder.Render(Bytes(_validGsv));

        Assert.IsNotNull(published);
        Assert.AreEqual("11", published!["satellitesInView"]);
    }

    [TestMethod]
    public void Render_Vtg_PublishesCourseAndBothSpeedUnits()
    {
        var decoder = new NmeaGpsDecoder();
        IReadOnlyDictionary<string, string>? published = null;
        decoder.ValuesChanged += (_, values) => published = values;

        decoder.Render(Bytes(_validVtg));

        Assert.IsNotNull(published);
        Assert.AreEqual("054.7", published!["courseTrue"]);
        Assert.AreEqual("005.5", published["speedKnots"]);
        Assert.AreEqual("010.2", published["speedKmh"]);
    }

    [TestMethod]
    public void Render_SameGgaTwice_OnlyPublishesOnFirstSentence()
    {
        var decoder = new NmeaGpsDecoder();
        var publishCount = 0;
        decoder.ValuesChanged += (_, _) => publishCount++;

        decoder.Render(Bytes(_validGga));
        decoder.Render(Bytes(_validGga));

        Assert.AreEqual(1, publishCount);
    }
}
