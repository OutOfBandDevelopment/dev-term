using System.IO;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Logging;
using DevTerm.Logging.Playback;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// WPF's Playback window over a real log file and the real presenter catalog, plus the main
/// window's logging controls. Driven through the windows' testable entry points (<c>Do</c>,
/// <c>Pump</c>, <c>StartLogging</c>, ...) on an STA thread without <c>Show()</c> — see
/// <see cref="MainWindowTests"/> for why — and an injected clock for timing.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
[DoNotParallelize]
public sealed class PlaybackWindowTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"devterm-wpf-playback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    /// <summary>The same capture as <c>DevTerm.Console.Tests.PlaybackModeTests.WriteSampleLog</c>, so both front ends' screenshots show the same thing.</summary>
    internal static string WriteSampleLog(string directory)
    {
        var path = Path.Combine(directory, "tek2230.jsonl");
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

    private static string[] Lines(PlaybackWindow window) => [.. window.OutputList.Items.Cast<PlaybackLine>().Select(l => l.Text)];

    private PlaybackWindow CreatePlaybackWindow(ManualTimeProvider? clock = null) =>
        new(new PlaybackPresenters().Open(WriteSampleLog(_directory), clock ?? new ManualTimeProvider())) { ShowInTaskbar = false };

    private static (MainWindow Window, FakeTransport Transport) CreateMainWindow(CliOptions? cliOptions = null)
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        var options = cliOptions ?? new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23" };
        options.Parser ??= presenter.Name;
        return (new MainWindow(session, new PresenterCatalog([presenter]), options, IsolatedProfiles.Empty()) { ShowInTaskbar = false }, transport);
    }

    [TestMethod]
    public void Opens_WithTheLogDescribed_ItsPresentersTicked_AndRealtimeSpeed()
    {
        StaTestRunner.Run(async () =>
        {
            var window = CreatePlaybackWindow();
            StaTestRunner.DoEvents();

            Assert.AreEqual("dev-term — Playback: tek2230.jsonl", window.Title);
            Assert.StartsWith("tek2230 (tcp://192.168.0.107:23)", window.DescriptionText.Text);
            var ticked = window.PresenterChoices.Items.Cast<PlaybackWindow.PresenterChoice>().Where(c => c.IsSelected).Select(c => c.Name).ToArray();
            CollectionAssert.AreEqual(new[] { "ascii" }, ticked);
            Assert.AreEqual("1x", ((PlaybackSpeed)window.SpeedBox.SelectedItem).Label);
            Assert.AreEqual(8, window.PositionSlider.Maximum);
            Assert.StartsWith("Paused  0/8", window.PositionText.Text);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void PlayAndPump_FollowTheInjectedClock_StylingEachKindOfLine()
    {
        StaTestRunner.Run(async () =>
        {
            var clock = new ManualTimeProvider();
            var window = CreatePlaybackWindow(clock);

            window.Do(window.Controller.TogglePlayPause);
            Assert.AreEqual("❚❚ _Pause", window.PlayButton.Content);

            clock.Advance(TimeSpan.FromSeconds(1.3));
            window.Pump();
            var lines = window.OutputList.Items.Cast<PlaybackLine>().ToArray();
            Assert.AreEqual("00:01.000 [tx] ID?\\r", lines[2].Text);
            Assert.AreEqual(PlaybackLineKind.Sent, lines[2].Kind);
            Assert.AreEqual("00:01.250 [ascii] ID TEK/2230,V81.1,VERS:14", lines[3].Text);
            Assert.AreEqual(5, (int)window.PositionSlider.Value);

            clock.Advance(TimeSpan.FromSeconds(10));
            window.Pump();
            Assert.AreEqual(PlaybackLineKind.Error, window.OutputList.Items.Cast<PlaybackLine>().Last().Kind);
            Assert.AreEqual("▶ _Play", window.PlayButton.Content);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void MovingTheSlider_Seeks_AndUntickingAPresenterReplaysThroughTheRest()
    {
        StaTestRunner.Run(async () =>
        {
            var window = CreatePlaybackWindow();

            window.PositionSlider.Value = 5;
            Assert.AreEqual(5, window.Controller.Engine.Position);

            var choices = window.PresenterChoices.Items.Cast<PlaybackWindow.PresenterChoice>().ToArray();
            choices.Single(c => c.Name == "hex").IsSelected = true;
            choices.Single(c => c.Name == "ascii").IsSelected = false;
            window.SetPresenterChoices();

            CollectionAssert.AreEqual(new[] { "hex" }, window.Controller.Presenters.ToArray());
            Assert.Contains("00:01.200 [hex] 49442054454B2F323233302C", Lines(window));
            Assert.AreEqual(5, window.Controller.Engine.Position, "The replay stops where playback was.");

            window.PositionSlider.Value = 2;
            Assert.AreEqual(2, window.Controller.Engine.Position);
            Assert.HasCount(2, Lines(window), "Seeking back clears and replays.");
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void AddNote_AndSaveSelection_WriteFiles()
    {
        StaTestRunner.Run(async () =>
        {
            var window = CreatePlaybackWindow();
            window.Do(() => window.Controller.SeekTo(2));
            window.Do(() =>
            {
                window.Controller.MarkIn();
                return PlaybackBatch.Empty;
            });

            window.NoteBox.Text = "query sent";
            window.AddNote();

            Assert.AreEqual(string.Empty, window.NoteBox.Text);
            Assert.AreEqual(PlaybackLineKind.Note, window.OutputList.Items.Cast<PlaybackLine>().Last().Kind);
            Assert.AreEqual("query sent", SessionLog.Load(window.Controller.Path).Records[2].Text);

            var trimmed = Path.Combine(_directory, "trimmed.jsonl");
            window.SaveSelection(trimmed);
            Assert.HasCount(7, SessionLog.Load(trimmed).Records, "Records 2 to 9 (the note included).");

            window.SaveSelection(window.Controller.Path);
            Assert.AreEqual(PlaybackLineKind.Error, window.OutputList.Items.Cast<PlaybackLine>().Last().Kind, "Overwriting the log being played is refused, not thrown.");
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void MainWindow_StartAndStopLogging_FlipTheMenuAndStatusBar_AndRecordTheConnection()
    {
        var path = Path.Combine(_directory, "wpf.jsonl");
        StaTestRunner.Run(async () =>
        {
            var (window, transport) = CreateMainWindow();
            StaTestRunner.DoEvents();
            Assert.AreEqual(MainWindow.StartLoggingHeader, window.LoggingMenuItem.Header);
            Assert.AreEqual(string.Empty, window.LoggingStatusText.Text);

            Assert.IsTrue(window.StartLogging(path));
            Assert.AreEqual(MainWindow.StopLoggingHeader, window.LoggingMenuItem.Header);
            Assert.AreEqual("● REC wpf.jsonl", window.LoggingStatusText.Text);

            await window.ConnectAsync();
            window.SendBox.Text = "ID?";
            await window.SendCurrentInputAsync();
            await transport.PushIncomingAsync("ID TEK\r"u8.ToArray());
            StaTestRunner.PumpUntil(() => window.OutputList.Items.Cast<OutputLine>().Any(l => l.Text.Contains("[ascii]", StringComparison.Ordinal)), TimeSpan.FromSeconds(5));
            await window.ToggleConnectionAsync();

            window.StopLogging();
            Assert.AreEqual(MainWindow.StartLoggingHeader, window.LoggingMenuItem.Header);
            Assert.AreEqual(string.Empty, window.LoggingStatusText.Text);
            Assert.IsNull(window.Logger);
        });

        var log = SessionLog.Load(path);
        Assert.AreEqual("Session,Open,Tx,Rx,Close", string.Join(",", log.Records.Select(r => r.Kind)));
        StringAssert.EndsWith(log.Header.Application, "(wpf)");
    }

    [TestMethod]
    public void MainWindow_TheLogOption_StartsLoggingAtConstruction()
    {
        var path = Path.Combine(_directory, "startup.jsonl");
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateMainWindow(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Log = path });

            Assert.AreEqual(path, window.Logger?.Path);
            window.StopLogging();
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void MainWindow_OpenPlayback_OfABadFile_ReportsItInsteadOfOpeningAWindow()
    {
        var bad = Path.Combine(_directory, "not-a-log.jsonl");
        File.WriteAllText(bad, "{\"hello\":1}\n");
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateMainWindow();

            Assert.IsNull(window.OpenPlayback(bad, show: false));
            var last = (OutputLine)window.OutputList.Items[^1]!;
            Assert.AreEqual(OutputKind.Error, last.Kind);
            Assert.Contains("Not a dev-term session log", last.Text);

            Assert.IsNotNull(window.OpenPlayback(WriteSampleLog(_directory), new ManualTimeProvider(), show: false));
            await Task.CompletedTask;
        });
    }
}
