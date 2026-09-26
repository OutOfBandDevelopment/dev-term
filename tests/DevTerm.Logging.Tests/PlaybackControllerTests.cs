using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Logging.Playback;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Logging.Tests;

/// <summary>Trim, markup and presenter choice — the Playback window's shared logic, over real files.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class PlaybackControllerTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] _available = ["ascii", "hex"];

    private string _directory = null!;
    private string _path = null!;

    [TestInitialize]
    public void CreateLog()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"devterm-playback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "capture.jsonl");
        new SessionLog(
            new SessionLogHeader { Created = _t0, Connection = "tcp://192.168.0.107:23", Presenters = ["ascii", "scpi"] },
            [.. Enumerable.Range(0, 6).Select(i => new SessionLogRecord
            {
                Kind = SessionLogRecordKind.Rx,
                Sequence = i + 1,
                Timestamp = _t0.AddSeconds(i),
                Data = Encoding.ASCII.GetBytes($"line{i}\r"),
            })]).Save(_path);
    }

    [TestCleanup]
    public void DeleteLog() => Directory.Delete(_directory, recursive: true);

    private static Pipeline PipelineFor(IReadOnlyList<string> names) =>
        new(names.Select(n => n == "hex"
            ? (IPresenter)new HexPresenter()
            : new AsciiPresenter(Options.Create(new AsciiPresenterOptions()))));

    private PlaybackController Open() => PlaybackController.Open(_path, PipelineFor, _available, new ManualTimeProvider());

    [TestMethod]
    public void StartsWithTheLogsOwnPresenters_LimitedToTheInstalledOnes()
    {
        var controller = Open();

        CollectionAssert.AreEqual(new[] { "ascii" }, controller.Presenters.ToArray());
        Assert.AreEqual("1x", controller.Speed.Label);
        Assert.AreEqual(0, controller.SelectionStart);
        Assert.AreEqual(6, controller.SelectionEnd);
    }

    [TestMethod]
    public void SetPresenters_IgnoresUnknownNames_AndReplaysThroughTheNewOnes()
    {
        var controller = Open();
        controller.SeekTo(2);

        var batch = controller.SetPresenters(["HEX", "nope"]);

        CollectionAssert.AreEqual(new[] { "hex" }, controller.Presenters.ToArray());
        Assert.IsTrue(batch.Reset);
        Assert.AreEqual("hex", batch.Items[0].Outputs[0].PresenterName);
        Assert.AreSame(PlaybackBatch.Empty, controller.SetPresenters(["nope"]), "Nothing valid chosen keeps the current presenters.");
    }

    [TestMethod]
    public void Trim_SavesTheSelectedRangeAsANewLog_KeepingSequenceNumbersAndProvenance()
    {
        var controller = Open();
        controller.SeekTo(1);
        controller.MarkIn();
        controller.SeekTo(4);
        controller.MarkOut();
        var trimmedPath = controller.DefaultTrimPath();

        controller.SaveSelection(trimmedPath);

        Assert.AreEqual(Path.Combine(_directory, "capture.trim-1-4.jsonl"), trimmedPath);
        var trimmed = SessionLog.Load(trimmedPath);
        CollectionAssert.AreEqual(new long?[] { 2, 3, 4 }, trimmed.Records.Select(r => r.Sequence).ToArray());
        Assert.AreEqual("capture.jsonl", trimmed.Header.TrimmedFrom);
        Assert.AreEqual("tcp://192.168.0.107:23", trimmed.Header.Connection);
        Assert.HasCount(6, SessionLog.Load(_path).Records, "The original is untouched.");
    }

    [TestMethod]
    public void Trim_RefusesToOverwriteTheLogBeingPlayed_OrAnEmptySelection()
    {
        var controller = Open();
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.SaveSelection(_path));

        controller.SeekTo(3);
        controller.MarkIn();
        controller.MarkOut();
        Assert.IsFalse(controller.HasSelection);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.SaveSelection(Path.Combine(_directory, "x.jsonl")));
    }

    [TestMethod]
    public void AddNote_IsSavedIntoTheLogFileImmediately_AndReloadsAtTheSamePosition()
    {
        var controller = Open();
        controller.SeekTo(2);

        var line = PlaybackText.Lines(controller.AddNote("  scope triggered here  ")).Single();

        Assert.AreEqual("00:01.000 [note] scope triggered here", line.Text);
        Assert.AreEqual(PlaybackLineKind.Note, line.Kind);
        var reloaded = SessionLog.Load(_path);
        Assert.HasCount(7, reloaded.Records);
        Assert.AreEqual(SessionLogRecordKind.Note, reloaded.Records[2].Kind);
        Assert.AreEqual("scope triggered here", reloaded.Records[2].Text);
        Assert.AreEqual(7, controller.SelectionEnd, "The selection end moved with the records after the note.");
    }

    [TestMethod]
    public void TogglePlayPause_AtTheEnd_StartsOverFromTheBeginning()
    {
        var controller = Open();
        controller.SkipToEnd();

        var batch = controller.TogglePlayPause();

        Assert.IsTrue(batch.Reset);
        Assert.IsTrue(controller.Engine.IsPlaying);
        Assert.AreEqual(0, controller.Engine.Position);
    }

    [TestMethod]
    public void PositionText_ShowsStateCountTimeSpeedAndSelection()
    {
        var controller = Open();
        controller.SeekTo(2);
        controller.SetSpeed(PlaybackController.Speeds[^1]);

        Assert.AreEqual("Paused  2/6  00:01.000 / 00:05.000  Max  Selection 0–6", controller.PositionText);
    }

    [TestMethod]
    public void Escape_ShowsControlAndNonAsciiBytesUnambiguously()
    {
        Assert.AreEqual(@"A\r\n\t\\\x00\xFF", PlaybackText.Escape([(byte)'A', 13, 10, 9, (byte)'\\', 0, 0xFF]));
    }
}
