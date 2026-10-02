using System.IO;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Loopback;
using Microsoft.Extensions.Options;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Captures successive stills of a paced loopback stream (<c>Samples: N</c> at a sample interval) arriving in the WPF
/// main window, for the user guide's loopback walkthrough; <c>scripts/make_loopback_gif.py</c> assembles them into an
/// animation. The TUI counterpart is <c>DevTerm.Console.Tests.LoopbackSampleStreamScreenshotTests</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class LoopbackSampleStreamScreenshotTests
{
    private const int _intervalMs = 400;
    private const int _samples = 6;
    private const string _prefix = "wpf-loopback-stream-";
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(10);

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public void PacedSamples_ArriveOneAtATime_StillsAreCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
            Directory.CreateDirectory(imagesDirectory);
            var transport = new LoopbackTransport(Options.Create(new LoopbackTransportOptions { SampleIntervalMs = _intervalMs }));
            var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
            var session = new Session(transport, new Pipeline([presenter]));
            var window = new MainWindow(session, new PresenterCatalog([presenter]), new CliOptions { Transport = "loopback", LoopbackSampleIntervalMs = _intervalMs, Parser = "ascii" }, IsolatedProfiles.Empty());
            WpfScreenshot.ShowOffScreen(window);
            StaTestRunner.PumpUntil(() => window.SendBox.IsEnabled, _pumpTimeout);

            // The transport holds the send until the last paced line is pushed, so run it unawaited.
            var send = session.SendAsync(Encoding.ASCII.GetBytes($"Samples: {_samples}\r"), TestContext.CancellationToken);

            for (var count = 1; count <= _samples; count++)
            {
                var wanted = count;
                Assert.IsTrue(StaTestRunner.PumpUntil(() => window.OutputList.Items.Count >= wanted, _pumpTimeout), $"Expected {wanted} sample line(s) to have arrived.");
                StaTestRunner.DoEvents();
                window.UpdateLayout();
                var path = Path.Combine(imagesDirectory, $"{_prefix}{count}.png");
                WpfScreenshot.Save(window, path);
                Assert.IsGreaterThan(1000L, new FileInfo(path).Length, "Expected a real rendered image.");
            }

            await send;
        });
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find DevTerm.slnx above the test output.");
    }
}
