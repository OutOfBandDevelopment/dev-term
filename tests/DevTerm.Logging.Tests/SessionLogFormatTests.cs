using System.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Logging.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class SessionLogFormatTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static SessionLogHeader Header() => new()
    {
        Created = _t0,
        Application = "dev-term 1.0.0 (tests)",
        Connection = "tcp://192.168.0.107:23",
        Profile = "tek2230",
        Transport = "tcp",
        Presenters = ["ascii", "hex"],
        Parser = "ascii",
    };

    private static SessionLog SampleLog()
    {
        var allBytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        return new SessionLog(Header(),
        [
            new() { Kind = SessionLogRecordKind.Session, Sequence = 1, Timestamp = _t0, Connection = "tcp://192.168.0.107:23", Profile = "tek2230", State = "closed" },
            new() { Kind = SessionLogRecordKind.Open, Sequence = 2, Timestamp = _t0.AddTicks(1234567) },
            new() { Kind = SessionLogRecordKind.Tx, Sequence = 3, Timestamp = _t0.AddSeconds(1), Data = "ID?\r"u8.ToArray() },
            new() { Kind = SessionLogRecordKind.Rx, Sequence = 4, Timestamp = _t0.AddSeconds(1.5), Data = allBytes },
            SessionLogRecord.Note(_t0.AddSeconds(1.5), "reply looks right — \"quoted\", ünïcode"),
            new() { Kind = SessionLogRecordKind.Disconnect, Sequence = 5, Timestamp = _t0.AddSeconds(2), Text = "Connection reset by peer" },
            new() { Kind = SessionLogRecordKind.Disconnect, Sequence = 6, Timestamp = _t0.AddSeconds(3) },
            new() { Kind = SessionLogRecordKind.Close, Sequence = 7, Timestamp = _t0.AddSeconds(4) },
        ]);
    }

    private static SessionLog RoundTrip(SessionLog log)
    {
        using var stream = new MemoryStream();
        log.Write(stream);
        stream.Position = 0;
        return SessionLog.Read(stream);
    }

    [TestMethod]
    public void Header_RoundTrips()
    {
        var read = RoundTrip(SampleLog()).Header;

        Assert.AreEqual(SessionLogHeader.CurrentVersion, read.Version);
        Assert.AreEqual(_t0, read.Created);
        Assert.AreEqual("dev-term 1.0.0 (tests)", read.Application);
        Assert.AreEqual("tcp://192.168.0.107:23", read.Connection);
        Assert.AreEqual("tek2230", read.Profile);
        Assert.AreEqual("tcp", read.Transport);
        CollectionAssert.AreEqual(new[] { "ascii", "hex" }, read.Presenters.ToArray());
        Assert.AreEqual("ascii", read.Parser);
        Assert.IsNull(read.TrimmedFrom);
    }

    [TestMethod]
    public void EveryRecordKind_RoundTripsExactly_IncludingEveryByteValueAndTickPrecisionTimestamps()
    {
        var original = SampleLog();
        var read = RoundTrip(original);

        Assert.HasCount(original.Records.Count, read.Records);
        for (var i = 0; i < original.Records.Count; i++)
        {
            var expected = original.Records[i];
            var actual = read.Records[i];
            Assert.AreEqual(expected.Kind, actual.Kind, $"record {i}");
            Assert.AreEqual(expected.Sequence, actual.Sequence, $"record {i}");
            Assert.AreEqual(expected.Timestamp, actual.Timestamp, $"record {i}");
            CollectionAssert.AreEqual(expected.Data.ToArray(), actual.Data.ToArray(), $"record {i}");
            Assert.AreEqual(expected.Text, actual.Text, $"record {i}");
            Assert.AreEqual(expected.Connection, actual.Connection, $"record {i}");
            Assert.AreEqual(expected.Profile, actual.Profile, $"record {i}");
            Assert.AreEqual(expected.State, actual.State, $"record {i}");
        }
    }

    [TestMethod]
    public void EachRecord_IsOneJsonLine_WithTheDocumentedFieldNames()
    {
        using var stream = new MemoryStream();
        SampleLog().Write(stream);
        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.HasCount(9, lines);
        Assert.StartsWith("{\"type\":\"header\",\"format\":\"dev-term-session-log\",\"version\":1,\"created\":\"2026-09-25T12:00:00.0000000Z\"", lines[0]);
        Assert.AreEqual("{\"type\":\"tx\",\"seq\":3,\"t\":\"2026-09-25T12:00:01.0000000Z\",\"data\":\"SUQ/DQ==\"}", lines[3]);
        Assert.AreEqual("{\"type\":\"note\",\"t\":\"2026-09-25T12:00:01.5000000Z\",\"text\":\"reply looks right — \\\"quoted\\\", ünïcode\"}", lines[5]);
        Assert.AreEqual("{\"type\":\"disconnect\",\"seq\":6,\"t\":\"2026-09-25T12:00:03.0000000Z\"}", lines[7]);
    }

    [TestMethod]
    public void AnUnknownRecordType_IsKeptVerbatim_AndWrittenBackUnchanged()
    {
        var future = "{\"type\":\"marker\",\"seq\":9,\"t\":\"2026-09-25T12:00:05.0000000Z\",\"colour\":\"red\"}";
        var text = SessionLogFormat.WriteHeader(Header()) + "\n" + future + "\n";
        var log = SessionLog.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.AreEqual(SessionLogRecordKind.Unknown, log.Records[0].Kind);
        Assert.AreEqual(future, SessionLogFormat.WriteRecord(log.Records[0]));
        Assert.AreEqual(future, RoundTrip(log).Records[0].RawJson);
    }

    [TestMethod]
    public void UnknownFields_AreIgnored()
    {
        var record = SessionLogFormat.ReadRecord("{\"type\":\"rx\",\"seq\":1,\"t\":\"2026-09-25T12:00:00Z\",\"data\":\"AAE=\",\"future\":42}");

        CollectionAssert.AreEqual(new byte[] { 0, 1 }, record.Data.ToArray());
    }

    [TestMethod]
    public void ANewerFormatVersion_IsRejectedWithAClearMessage()
    {
        var ex = Assert.ThrowsExactly<SessionLogFormatException>(() =>
            SessionLogFormat.ReadHeader("{\"type\":\"header\",\"format\":\"dev-term-session-log\",\"version\":2,\"created\":\"2026-09-25T12:00:00Z\"}"));

        Assert.Contains("version 2", ex.Message);
    }

    [TestMethod]
    public void AFileThatIsNotASessionLog_IsRejected()
    {
        Assert.ThrowsExactly<SessionLogFormatException>(() => SessionLog.Read(new MemoryStream("{\"hello\":1}\n"u8.ToArray())));
        Assert.ThrowsExactly<SessionLogFormatException>(() => SessionLog.Read(new MemoryStream("not json\n"u8.ToArray())));
        Assert.ThrowsExactly<SessionLogFormatException>(() => SessionLog.Read(new MemoryStream([])));
    }

    [TestMethod]
    public void ATornFinalLine_IsSkippedWithAWarning_SoACaptureCutShortStillLoads()
    {
        var text = SessionLogFormat.WriteHeader(Header()) + "\n"
            + "{\"type\":\"open\",\"seq\":1,\"t\":\"2026-09-25T12:00:00Z\"}\n"
            + "{\"type\":\"rx\",\"seq\":2,\"t\":\"2026-09-25T12:0";

        var log = SessionLog.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.HasCount(1, log.Records);
        Assert.HasCount(1, log.Warnings);
        Assert.Contains("Line 3", log.Warnings[0]);
    }

    [TestMethod]
    public void AMalformedLineBeforeTheLast_FailsTheLoad_NamingTheLine()
    {
        var text = SessionLogFormat.WriteHeader(Header()) + "\n"
            + "{\"type\":\"rx\",\"seq\":1,\"t\":\"2026-09-25T12:00:00Z\"}\n"
            + "{\"type\":\"open\",\"seq\":2,\"t\":\"2026-09-25T12:00:00Z\"}\n";

        var ex = Assert.ThrowsExactly<SessionLogFormatException>(() => SessionLog.Read(new MemoryStream(Encoding.UTF8.GetBytes(text))));

        Assert.Contains("Line 2", ex.Message);
        Assert.Contains("base64", ex.Message);
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripThroughARealFile_ReplacingItAtomically()
    {
        var path = Path.Combine(Path.GetTempPath(), $"devterm-log-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(path, "old content");
            SampleLog().Save(path);

            var loaded = SessionLog.Load(path);

            Assert.HasCount(SampleLog().Records.Count, loaded.Records);
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
