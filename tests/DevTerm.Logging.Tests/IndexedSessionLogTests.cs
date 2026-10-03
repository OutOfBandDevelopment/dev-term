using System.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Logging.Tests;

/// <summary><see cref="SessionLog.OpenIndexed"/>: the same log as <see cref="SessionLog.Load"/>, read from disk on demand.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class IndexedSessionLogTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private string _path = "";

    [TestInitialize]
    public void Init() => _path = Path.Combine(Path.GetTempPath(), $"devterm-idx-{Guid.NewGuid():N}.jsonl");

    [TestCleanup]
    public void Cleanup() => File.Delete(_path);

    private static SessionLog Make(int rx)
    {
        var records = new List<SessionLogRecord> { new() { Kind = SessionLogRecordKind.Open, Sequence = 1, Timestamp = _t0 } };
        for (var i = 0; i < rx; i++)
        {
            records.Add(new SessionLogRecord { Kind = SessionLogRecordKind.Rx, Sequence = i + 2, Timestamp = _t0.AddMilliseconds(i * 10), Data = Encoding.UTF8.GetBytes($"line {i} ünï\r\n") });
        }

        return new SessionLog(new SessionLogHeader { Created = _t0, Presenters = ["ascii"] }, records);
    }

    [TestMethod]
    public void OpenIndexed_GivesTheSameRecordsOffsetsAndDurationAsLoad_AcrossPageBoundaries()
    {
        Make(1000).Save(_path);

        var loaded = SessionLog.Load(_path);
        var indexed = SessionLog.OpenIndexed(_path);

        Assert.IsTrue(indexed.IsIndexed);
        Assert.AreEqual(loaded.Records.Count, indexed.Records.Count);
        Assert.AreEqual(loaded.Start, indexed.Start);
        Assert.AreEqual(loaded.Duration, indexed.Duration);
        foreach (var i in new[] { 0, 1, 255, 256, 257, 700, 1000 })
        {
            Assert.AreEqual(loaded.Records[i].Sequence, indexed.Records[i].Sequence);
            Assert.AreEqual(loaded.OffsetOf(i), indexed.OffsetOf(i));
            CollectionAssert.AreEqual(loaded.Records[i].Data.ToArray(), indexed.Records[i].Data.ToArray());
        }

        Assert.AreEqual(1001, indexed.Records.Count());
    }

    [TestMethod]
    public void OpenIndexed_HandlesCrlfBomBlankLinesAndATornFinalLine()
    {
        var text = "﻿" + SessionLogFormat.WriteHeader(new SessionLogHeader { Created = _t0 }) + "\r\n\r\n"
            + "{\"type\":\"open\",\"seq\":1,\"t\":\"2026-10-03T12:00:00Z\"}\r\n\r\n"
            + "{\"type\":\"close\",\"seq\":2,\"t\":\"2026-10-03T12:00:01Z\"}\r\n"
            + "{\"type\":\"rx\",\"seq\":3,\"t\":\"2026-10-0";
        File.WriteAllText(_path, text, new UTF8Encoding(false));

        var log = SessionLog.OpenIndexed(_path);

        Assert.HasCount(2, log.Records);
        Assert.AreEqual(SessionLogRecordKind.Close, log.Records[1].Kind);
        Assert.HasCount(1, log.Warnings);
        Assert.AreEqual(TimeSpan.FromSeconds(1), log.Duration);
    }

    [TestMethod]
    public void OpenIndexed_AMalformedMiddleLine_FailsNamingIt()
    {
        var text = SessionLogFormat.WriteHeader(new SessionLogHeader { Created = _t0 }) + "\n"
            + "{\"type\":\"rx\",\"seq\":1,\"t\":\"2026-10-03T12:00:00Z\"}\n"
            + "{\"type\":\"open\",\"seq\":2,\"t\":\"2026-10-03T12:00:00Z\"}\n";
        File.WriteAllText(_path, text);

        var ex = Assert.ThrowsExactly<SessionLogFormatException>(() => SessionLog.OpenIndexed(_path));

        Assert.Contains("Line 2", ex.Message);
    }

    [TestMethod]
    public void InsertNote_OnAnIndexedLog_MaterialisesAndKeepsEveryRecord()
    {
        Make(300).Save(_path);
        var log = SessionLog.OpenIndexed(_path);

        log.InsertNote(2, "look here");

        Assert.IsFalse(log.IsIndexed);
        Assert.HasCount(302, log.Records);
        Assert.AreEqual("look here", log.Records[2].Text);
        Assert.AreEqual(3L, log.Records[3].Sequence);
    }

    [TestMethod]
    public void TheIndexedFile_CanBeReplacedWhileOpen_SoANoteSaveInPlaceWorks()
    {
        Make(50).Save(_path);
        var indexed = SessionLog.OpenIndexed(_path);
        _ = indexed.Records[10];

        Make(60).Save(_path);

        Assert.AreEqual(51, indexed.Records.Count);
    }
}
