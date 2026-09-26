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

        Assert.AreEqual(Path.Combine("~", ".dev-term", "exports"), StreamMonitor.DisplayPath(DevTermUserDataPaths.ExportsDirectory));
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
}
