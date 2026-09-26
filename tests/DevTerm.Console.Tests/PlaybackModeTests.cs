using System.Text;
using DevTerm.Configuration;
using DevTerm.Logging;
using DevTerm.Logging.Playback;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI Playback window over a real log file and the real presenter catalog
/// (<see cref="PlaybackPresenters"/>), driven headlessly: buttons' actions go through
/// <see cref="PlaybackWindowParts.Do"/> exactly as their Accepting handlers do, and the timer tick
/// through <see cref="PlaybackWindowParts.Pump"/> with an injected clock.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
[DoNotParallelize]
public sealed class PlaybackModeTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"devterm-tui-playback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    /// <summary>A small, realistic capture: connect, a query, its reply split across two reads, a second query 5 s later, a hang-up.</summary>
    internal static string WriteSampleLog(string directory, string name = "tek2230.jsonl")
    {
        var path = Path.Combine(directory, name);
        new SessionLog(
            new SessionLogHeader { Created = _t0, Connection = "tcp://192.168.0.107:23", Profile = "tek2230", Transport = "tcp", Presenters = ["ascii"], Parser = "ascii" },
            [
                new() { Kind = SessionLogRecordKind.Session, Sequence = 1, Timestamp = _t0, Connection = "tcp://192.168.0.107:23", Profile = "tek2230", State = "closed" },
                new() { Kind = SessionLogRecordKind.Open, Sequence = 2, Timestamp = _t0.AddMilliseconds(40) },
                new() { Kind = SessionLogRecordKind.Tx, Sequence = 3, Timestamp = _t0.AddSeconds(1), Data = Encoding.ASCII.GetBytes("ID?\r") },
                new() { Kind = SessionLogRecordKind.Rx, Sequence = 4, Timestamp = _t0.AddMilliseconds(1200), Data = Encoding.ASCII.GetBytes("ID TEK/2230,") },
                new() { Kind = SessionLogRecordKind.Rx, Sequence = 5, Timestamp = _t0.AddMilliseconds(1250), Data = Encoding.ASCII.GetBytes("V81.1,VERS:14\r") },
                new() { Kind = SessionLogRecordKind.Tx, Sequence = 6, Timestamp = _t0.AddSeconds(6), Data = Encoding.ASCII.GetBytes("CH1?\r") },
                new() { Kind = SessionLogRecordKind.Rx, Sequence = 7, Timestamp = _t0.AddMilliseconds(6300), Data = Encoding.ASCII.GetBytes("CH1 VOLTS:1,COUPLING:DC\r") },
                new() { Kind = SessionLogRecordKind.Disconnect, Sequence = 8, Timestamp = _t0.AddSeconds(9) },
            ]).Save(path);
        return path;
    }

    private static void RunWindow(PlaybackController controller, Action<PlaybackWindowParts> body) =>
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = PlaybackMode.BuildWindow(app, controller);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                body(parts);
            }
            finally
            {
                app.End(token);
            }
        });

    [TestMethod]
    public void OpensWithTheLogDescribed_AndItsOwnPresenters()
    {
        var controller = new PlaybackPresenters().Open(WriteSampleLog(_directory), new ManualTimeProvider());

        RunWindow(controller, parts =>
        {
            var screen = TuiTestRunner.DumpBuffer();
            Assert.Contains("Playback: tek2230.jsonl", screen);
            Assert.Contains("tek2230 (tcp://192.168.0.107:23)", screen);
            Assert.AreEqual("ascii", parts.PresentersField.Text);
            Assert.StartsWith("Paused  0/8", parts.PositionLabel.Text);
        });
    }

    [TestMethod]
    public void Step_ShowsEachRecord_AndTheAsciiLineOnlyOnceItIsComplete()
    {
        var controller = new PlaybackPresenters().Open(WriteSampleLog(_directory), new ManualTimeProvider());

        RunWindow(controller, parts =>
        {
            for (var i = 0; i < 5; i++)
            {
                parts.Do(controller.Step);
            }

            var text = parts.Output.Text;
            Assert.Contains("00:00.040 [dev-term] Connected.", text);
            Assert.Contains("00:01.000 [tx] ID?\\r", text);
            Assert.Contains("00:01.250 [ascii] ID TEK/2230,V81.1,VERS:14", text);
            Assert.DoesNotContain("[ascii] ID TEK/2230,\n", text);
            Assert.StartsWith("Paused  5/8  00:01.250 / 00:09.000", parts.PositionLabel.Text);
        });
    }

    [TestMethod]
    public void Pump_PlaysInRealtimeAgainstTheClock_AndPlayFlipsToPause()
    {
        var clock = new ManualTimeProvider();
        var controller = new PlaybackPresenters().Open(WriteSampleLog(_directory), clock);

        RunWindow(controller, parts =>
        {
            parts.Do(controller.TogglePlayPause);
            Assert.AreEqual("_Pause", parts.PlayButton.Text);

            clock.Advance(TimeSpan.FromSeconds(1.3));
            parts.Pump();
            Assert.Contains("[ascii] ID TEK/2230,V81.1,VERS:14", parts.Output.Text);
            Assert.DoesNotContain("CH1?", parts.Output.Text);

            clock.Advance(TimeSpan.FromSeconds(10));
            parts.Pump();
            Assert.Contains("[error] The device closed the connection.", parts.Output.Text);
            Assert.AreEqual("_Play", parts.PlayButton.Text, "Back to Play at the end.");
        });
    }

    [TestMethod]
    public void Rewind_ClearsTheOutput_AndChangingPresentersReplaysThroughThem()
    {
        var controller = new PlaybackPresenters().Open(WriteSampleLog(_directory), new ManualTimeProvider());

        RunWindow(controller, parts =>
        {
            parts.Do(controller.SkipToEnd);
            parts.Do(controller.Rewind);
            Assert.AreEqual(string.Empty, parts.Output.Text);

            parts.Do(() => controller.SeekTo(4));
            parts.Do(() => controller.SetPresenters(["hex", "ascii"]));

            Assert.AreEqual("hex, ascii", parts.PresentersField.Text);
            Assert.Contains("[hex] 49442054454B2F323233302C", parts.Output.Text);
            Assert.StartsWith("Paused  4/8", parts.PositionLabel.Text);
        });
    }

    [TestMethod]
    public void AddNote_ShowsImmediately_AndIsWrittenToTheLog()
    {
        var path = WriteSampleLog(_directory);
        var controller = new PlaybackPresenters().Open(path, new ManualTimeProvider());

        RunWindow(controller, parts =>
        {
            parts.Do(() => controller.SeekTo(5));
            parts.Do(() => controller.AddNote("IDN reply is correct"));

            Assert.Contains("00:01.250 [note] IDN reply is correct", parts.Output.Text);
        });

        Assert.AreEqual("IDN reply is correct", SessionLog.Load(path).Records[5].Text);
    }
}
