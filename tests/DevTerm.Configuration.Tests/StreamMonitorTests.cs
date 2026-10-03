using System.IO.Pipelines;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.StreamContent;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class StreamMonitorTests
{
    private string _exportDirectory = null!;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void CreateExportDirectory() =>
        _exportDirectory = Path.Combine(Path.GetTempPath(), "devterm-streammonitor-tests-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void DeleteExportDirectory()
    {
        if (Directory.Exists(_exportDirectory))
        {
            Directory.Delete(_exportDirectory, recursive: true);
        }
    }

    private sealed class LiveSession : IAsyncDisposable
    {
        private readonly Pipe _pipe = new();

        public LiveSession()
        {
            var transport = new Mock<ITransport>();
            transport.SetupGet(t => t.Input).Returns(_pipe.Reader);
            transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
            Session = new Session(transport.Object, new Pipeline([]));
        }

        public Session Session { get; }

        public async Task SendFromDeviceAsync(byte[] data, CancellationToken cancellationToken) =>
            await _pipe.Writer.WriteAsync(data, cancellationToken);

        public async ValueTask DisposeAsync() => await Session.DisposeAsync();
    }

    private static FakeTimeProvider FixedTime() =>
        new(new DateTimeOffset(2026, 9, 25, 14, 35, 12, TimeSpan.Zero));

    private static Task<StreamMonitorCapture> NextCaptureAsync(StreamMonitor monitor)
    {
        var next = new TaskCompletionSource<StreamMonitorCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, StreamMonitorCapture capture)
        {
            monitor.CaptureAdded -= Handler;
            next.TrySetResult(capture);
        }

        monitor.CaptureAdded += Handler;
        return next.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    [DataRow("tcp://192.168.0.5:5025", "tcp_192.168.0.5_5025")]
    [DataRow("HP 34401A", "HP_34401A")]
    [DataRow("serial://COM3:9600,8,n,1", "serial_COM3_9600_8_n_1")]
    [DataRow("  //  ", "device")]
    [DataRow("bench-scope_2", "bench-scope_2")]
    public void SanitizeForFileName_KeepsOnlySafeCharacters(string name, string expected) =>
        Assert.AreEqual(expected, StreamMonitor.SanitizeForFileName(name));

    [TestMethod]
    public void DisplayPath_ShortensTheHomeFolderToTilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.AreEqual(Path.Combine("~", ".dev-term", "exports"), StreamMonitor.DisplayPath(Path.Combine(home, ".dev-term", "exports")));
        Assert.AreEqual("~", StreamMonitor.DisplayPath(home));
        Assert.AreEqual(home + "-other", StreamMonitor.DisplayPath(home + "-other"));
        Assert.AreEqual(@"D:\captures", StreamMonitor.DisplayPath(@"D:\captures"));
    }

    [TestMethod]
    public void FileNameFor_IsDeviceTimestampExtension() =>
        Assert.AreEqual(
            "hp34401a_20260923-143512.bmp",
            StreamMonitor.FileNameFor("hp34401a", new DateTimeOffset(2026, 9, 23, 14, 35, 12, TimeSpan.FromHours(-5)), "bmp"));

    [TestMethod]
    public void DeviceNameFor_PrefersTheSavedProfileName_ElseTheConnectionDefinition()
    {
        var profiles = Path.Combine(_exportDirectory, "profiles");
        var store = new ConnectionProfileStore(profiles);
        var saved = new CliOptions { Transport = "tcp", Host = "192.168.0.5", Port = "5025" };
        store.Save("Bench DMM", saved);

        Assert.AreEqual("Bench DMM", StreamMonitor.DeviceNameFor(saved, store));
        Assert.AreEqual("tcp://192.168.0.6:5025", StreamMonitor.DeviceNameFor(new CliOptions { Transport = "tcp", Host = "192.168.0.6", Port = "5025" }, store));
    }

    [TestMethod]
    public void Start_BeforeSetSession_Throws()
    {
        using var monitor = new StreamMonitor();

        Assert.ThrowsExactly<InvalidOperationException>(monitor.Start);
    }

    [TestMethod]
    public async Task Running_DetectedImage_IsAutoSavedUnderTheNamingConvention()
    {
        await using var live = new LiveSession();
        using var monitor = new StreamMonitor(FixedTime());
        monitor.SetSession(live.Session, "tcp://192.168.0.5:5025", _exportDirectory);
        monitor.Start();
        await live.Session.OpenAsync(TestContext.CancellationToken);

        var next = NextCaptureAsync(monitor);
        var png = StreamContentSamples.Png();
        await live.SendFromDeviceAsync([.. "ok\r\n"u8, .. png], TestContext.CancellationToken);
        var capture = await next;

        Assert.AreEqual(Path.Combine(_exportDirectory, "tcp_192.168.0.5_5025_20260925-143512.png"), capture.SavedPath);
        CollectionAssert.AreEqual(png, File.ReadAllBytes(capture.SavedPath!));
        Assert.AreEqual(StreamContentKind.Png, capture.Capture.Kind);
        Assert.HasCount(1, monitor.Captures);
        StringAssert.StartsWith(capture.Describe(), $"Captured {png.Length} bytes of PNG image to ");
    }

    [TestMethod]
    public async Task TwoCapturesInTheSameSecond_DoNotOverwriteEachOther()
    {
        await using var live = new LiveSession();
        using var monitor = new StreamMonitor(FixedTime());
        monitor.SetSession(live.Session, "scope", _exportDirectory);
        monitor.Start();
        await live.Session.OpenAsync(TestContext.CancellationToken);

        var first = NextCaptureAsync(monitor);
        await live.SendFromDeviceAsync(StreamContentSamples.Bmp(), TestContext.CancellationToken);
        await first;
        var second = NextCaptureAsync(monitor);
        await live.SendFromDeviceAsync(StreamContentSamples.Bmp(), TestContext.CancellationToken);
        await second;

        Assert.AreSequenceEqual(
            ["scope_20260925-143512-2.bmp", "scope_20260925-143512.bmp"],
            [.. Directory.GetFiles(_exportDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal)]);
    }

    [TestMethod]
    public async Task NotRunning_CapturesNothing_AndBindsNothingIntoThePipeline()
    {
        await using var live = new LiveSession();
        using var monitor = new StreamMonitor(FixedTime());
        monitor.SetSession(live.Session, "scope", _exportDirectory);

        Assert.IsFalse(monitor.IsRunning);
        Assert.IsEmpty(live.Session.Presenters);
    }

    [TestMethod]
    public async Task Stop_FlushesAndSavesAPartialCapture_AndUnbindsTheWatcher()
    {
        await using var live = new LiveSession();
        using var monitor = new StreamMonitor(FixedTime());
        monitor.SetSession(live.Session, "scope", _exportDirectory);
        monitor.Start();
        Assert.HasCount(1, live.Session.Presenters);
        await live.Session.OpenAsync(TestContext.CancellationToken);

        // Half a PNG, then wait for the read loop to have seen it before stopping.
        await live.SendFromDeviceAsync(StreamContentSamples.Png()[..20], TestContext.CancellationToken);
        var watcher = (StreamContentWatcher)live.Session.Presenters[0];
        Assert.IsTrue(SpinWait.SpinUntil(() => watcher.IsCapturing, TimeSpan.FromSeconds(5)));

        var flushed = NextCaptureAsync(monitor);
        monitor.Stop();
        var capture = await flushed;

        Assert.AreEqual(StreamCaptureEnd.Flushed, capture.Capture.EndReason);
        Assert.IsTrue(File.Exists(capture.SavedPath));
        Assert.IsFalse(monitor.IsRunning);
        Assert.IsEmpty(live.Session.Presenters);
    }

    [TestMethod]
    public async Task SetSession_WhileRunning_MovesTheWatcherToTheNewSession()
    {
        await using var oldSession = new LiveSession();
        await using var newSession = new LiveSession();
        using var monitor = new StreamMonitor(FixedTime());
        monitor.SetSession(oldSession.Session, "old", _exportDirectory);
        monitor.Start();

        monitor.SetSession(newSession.Session, "new device", _exportDirectory);

        Assert.IsTrue(monitor.IsRunning);
        Assert.IsEmpty(oldSession.Session.Presenters);
        Assert.HasCount(1, newSession.Session.Presenters);
        Assert.IsInstanceOfType<IStreamContentHintSink>(newSession.Session.Presenters[0]);

        await newSession.Session.OpenAsync(TestContext.CancellationToken);
        var next = NextCaptureAsync(monitor);
        await newSession.SendFromDeviceAsync(StreamContentSamples.Gif(), TestContext.CancellationToken);
        var capture = await next;

        Assert.AreEqual("new device", capture.DeviceName);
        StringAssert.EndsWith(capture.SavedPath, "new_device_20260925-143512.gif");
    }

    [TestMethod]
    public async Task Track_TwoSessions_WatchesBothAndLabelsEachCaptureWithItsOwnDeviceAndFolder()
    {
        await using var first = new LiveSession();
        await using var second = new LiveSession();
        var secondDirectory = Path.Combine(_exportDirectory, "second");
        using var monitor = new StreamMonitor(FixedTime());
        var firstKey = new object();
        var secondKey = new object();
        monitor.Track(firstKey, first.Session, "scope", _exportDirectory);
        monitor.Track(secondKey, second.Session, "meter", secondDirectory);

        monitor.Start();

        Assert.HasCount(1, first.Session.Presenters);
        Assert.HasCount(1, second.Session.Presenters);
        Assert.AreEqual(2, monitor.SessionCount);
        Assert.AreEqual("scope, meter", monitor.DeviceName);
        Assert.HasCount(2, monitor.ExportDirectories);

        await first.Session.OpenAsync(TestContext.CancellationToken);
        await second.Session.OpenAsync(TestContext.CancellationToken);
        var next = NextCaptureAsync(monitor);
        await first.SendFromDeviceAsync(StreamContentSamples.Gif(), TestContext.CancellationToken);
        var fromFirst = await next;
        next = NextCaptureAsync(monitor);
        await second.SendFromDeviceAsync(StreamContentSamples.Gif(), TestContext.CancellationToken);
        var fromSecond = await next;

        Assert.AreEqual("scope", fromFirst.DeviceName);
        Assert.AreSame(firstKey, fromFirst.Source);
        Assert.AreEqual(_exportDirectory, Path.GetDirectoryName(fromFirst.SavedPath));
        Assert.AreEqual("meter", fromSecond.DeviceName);
        Assert.AreSame(secondKey, fromSecond.Source);
        Assert.AreEqual(secondDirectory, Path.GetDirectoryName(fromSecond.SavedPath));
        Assert.HasCount(2, monitor.Captures);
    }

    [TestMethod]
    public async Task Track_WhileRunning_WatchesTheNewSessionAtOnce_AndUntrackStopsWatchingIt()
    {
        await using var first = new LiveSession();
        await using var second = new LiveSession();
        using var monitor = new StreamMonitor(FixedTime());
        monitor.Track("first", first.Session, "one", _exportDirectory);
        monitor.Start();

        monitor.Track("second", second.Session, "two", _exportDirectory);

        Assert.HasCount(1, second.Session.Presenters);

        monitor.Untrack("second");

        Assert.IsEmpty(second.Session.Presenters);
        Assert.HasCount(1, first.Session.Presenters);
        Assert.AreEqual(1, monitor.SessionCount);
        Assert.IsTrue(monitor.IsRunning);
    }

    [TestMethod]
    public async Task StartAndStop_ApplyToEverySession()
    {
        await using var first = new LiveSession();
        await using var second = new LiveSession();
        using var monitor = new StreamMonitor(FixedTime());
        monitor.Track("first", first.Session, "one", _exportDirectory);
        monitor.Track("second", second.Session, "two", _exportDirectory);

        monitor.Start();
        monitor.Stop();

        Assert.IsFalse(monitor.IsRunning);
        Assert.IsEmpty(first.Session.Presenters);
        Assert.IsEmpty(second.Session.Presenters);
    }

    [TestMethod]
    public async Task SaveFailure_IsReportedOnTheCapture_NotThrown()
    {
        Directory.CreateDirectory(_exportDirectory);
        var notADirectory = Path.Combine(_exportDirectory, "a-file");
        await File.WriteAllTextAsync(notADirectory, "x", TestContext.CancellationToken);

        await using var live = new LiveSession();
        using var monitor = new StreamMonitor(FixedTime());
        monitor.SetSession(live.Session, "scope", notADirectory);
        monitor.Start();
        await live.Session.OpenAsync(TestContext.CancellationToken);

        var next = NextCaptureAsync(monitor);
        await live.SendFromDeviceAsync(StreamContentSamples.Bmp(), TestContext.CancellationToken);
        var capture = await next;

        Assert.IsNull(capture.SavedPath);
        Assert.IsFalse(string.IsNullOrEmpty(capture.SaveError));
        StringAssert.Contains(capture.Describe(), "could not save it");
        Assert.AreEqual(ConnectionState.Open, live.Session.State);
    }

    [TestMethod]
    public async Task LoadFromDisk_ListsEarlierExports_OnceAndOldestFirst()
    {
        await using var live = new LiveSession();
        using var monitor = new StreamMonitor();
        monitor.SetSession(live.Session, "scope", _exportDirectory);
        Directory.CreateDirectory(_exportDirectory);
        File.WriteAllBytes(Path.Combine(_exportDirectory, "scope_20260102-030405.bmp"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_exportDirectory, "scope_20250102-030405-2.png"), [4, 5]);

        Assert.AreEqual(2, monitor.LoadFromDisk());
        Assert.AreEqual(0, monitor.LoadFromDisk());

        var captures = monitor.Captures;
        Assert.HasCount(2, captures);
        Assert.AreEqual("scope", captures[0].DeviceName);
        Assert.AreEqual(2025, captures[0].LocalStartedAt.Year);
        Assert.AreEqual("PNG image", captures[0].Capture.Kind.DisplayName);
        Assert.AreEqual(3, captures[1].Capture.Data.Length);
    }

    [TestMethod]
    [DataRow(true, 2)]
    [DataRow(false, 1)]
    public async Task HpglCapture_IsConvertedToSvgAutomatically_WhenEnabled(bool auto, int expectedCaptures)
    {
        await using var live = new LiveSession();
        var options = new StreamContentWatcherOptions { IdleTimeout = TimeSpan.FromMilliseconds(100), HpglIdleTimeout = TimeSpan.FromMilliseconds(100) };
        using var monitor = new StreamMonitor(FixedTime(), options) { AutoConvertHpgl = auto };
        monitor.SetSession(live.Session, "plotter", _exportDirectory);
        monitor.Start();
        await live.Session.OpenAsync(TestContext.CancellationToken);

        var first = NextCaptureAsync(monitor);
        await live.SendFromDeviceAsync("ok\r\nIN;SP1;PU0,0;PD1000,1000;PD2000,0;SP0;"u8.ToArray(), TestContext.CancellationToken);
        var plot = await first;
        await Task.Delay(300, TestContext.CancellationToken);

        Assert.AreEqual(StreamContentKind.Hpgl, plot.Capture.Kind);
        Assert.HasCount(expectedCaptures, monitor.Captures);
        Assert.AreEqual(auto, File.Exists(Path.ChangeExtension(plot.SavedPath!, "svg")));
    }

    [TestMethod]
    public async Task CaptureExport_Newest_TakesTheLastN_OldestFirst()
    {
        await using var live = new LiveSession();
        using var monitor = new StreamMonitor();
        monitor.SetSession(live.Session, "scope", _exportDirectory);
        Directory.CreateDirectory(_exportDirectory);
        foreach (var day in new[] { "01", "02", "03" })
        {
            File.WriteAllText(Path.Combine(_exportDirectory, $"scope_202601{day}-100000.png"), day);
        }

        monitor.LoadFromDisk();
        var newest = CaptureExport.Newest(monitor.Captures, 2);

        CollectionAssert.AreEqual(new[] { "scope_20260102-100000.png", "scope_20260103-100000.png" }, newest.Select(c => Path.GetFileName(c.SavedPath)).ToArray());
        Assert.AreEqual(0, CaptureExport.Newest(monitor.Captures, 0).Count);
    }

    [TestMethod]
    public async Task CaptureExport_CopyTo_CopiesFiles_AndNeverOverwrites()
    {
        await using var live = new LiveSession();
        using var monitor = new StreamMonitor();
        monitor.SetSession(live.Session, "scope", _exportDirectory);
        Directory.CreateDirectory(_exportDirectory);
        File.WriteAllText(Path.Combine(_exportDirectory, "scope_20260101-100000.png"), "x");
        monitor.LoadFromDisk();
        var target = Path.Combine(Path.GetTempPath(), "devterm-export-out-" + Guid.NewGuid().ToString("N"));

        var first = CaptureExport.CopyTo(monitor.Captures, target);
        var second = CaptureExport.CopyTo(monitor.Captures, target);

        Assert.AreEqual(1, first.Count);
        StringAssert.EndsWith(second.Single(), "scope_20260101-100000-2.png");
        Assert.AreEqual(2, Directory.GetFiles(target).Length);
    }
}
