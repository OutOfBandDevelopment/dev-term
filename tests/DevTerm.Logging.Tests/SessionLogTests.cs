using DevTerm.Test.Utilities;

namespace DevTerm.Logging.Tests;

/// <summary>A whole in-memory log's <see cref="SessionLog.Start"/>/<see cref="SessionLog.OffsetOf"/> - see <see cref="SessionLog"/>.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class SessionLogTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static SessionLogRecord Rx(double seconds) =>
        new() { Kind = SessionLogRecordKind.Rx, Sequence = 1, Timestamp = _t0.AddSeconds(seconds), Data = new byte[] { 1 } };

    private static SessionLogRecord UnknownWithNoTimestamp() =>
        new() { Kind = SessionLogRecordKind.Unknown, Timestamp = DateTimeOffset.MinValue, RawJson = "{\"type\":\"future\"}" };

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Start_FirstRecordIsUnknownWithNoTimestamp_SkipsItInsteadOfUsingMinValue()
    {
        // Regression test for bug 041: a record type this version doesn't know (SessionLogRecordKind.Unknown)
        // gets DateTimeOffset.MinValue when it has no "t" field. If that record happens to be first, Start
        // used to become MinValue, so every real record's offset came out as billions of seconds - effectively
        // an infinite wait in 1x playback. See docs/bugs/resolved/041-unknown-record-min-timestamp.md.
        var log = new SessionLog(
            new SessionLogHeader { Created = _t0, Presenters = ["ascii"] },
            [UnknownWithNoTimestamp(), Rx(0), Rx(5)]);

        Assert.AreEqual(_t0, log.Start);
        Assert.AreEqual(TimeSpan.FromSeconds(5), log.OffsetOf(2));
    }

    [TestMethod]
    public void Start_AllRecordsAreUnknownWithNoTimestamp_FallsBackToHeaderCreated()
    {
        var log = new SessionLog(
            new SessionLogHeader { Created = _t0, Presenters = ["ascii"] },
            [UnknownWithNoTimestamp(), UnknownWithNoTimestamp()]);

        Assert.AreEqual(_t0, log.Start);
    }

    [TestMethod]
    public void Start_NoRecords_UsesHeaderCreated()
    {
        var log = new SessionLog(new SessionLogHeader { Created = _t0, Presenters = ["ascii"] }, []);

        Assert.AreEqual(_t0, log.Start);
    }

    [TestMethod]
    public void Start_FirstRecordHasARealTimestamp_UsesIt()
    {
        var log = new SessionLog(
            new SessionLogHeader { Created = _t0, Presenters = ["ascii"] },
            [Rx(0), Rx(3)]);

        Assert.AreEqual(_t0, log.Start);
        Assert.AreEqual(TimeSpan.FromSeconds(3), log.OffsetOf(1));
    }
}
