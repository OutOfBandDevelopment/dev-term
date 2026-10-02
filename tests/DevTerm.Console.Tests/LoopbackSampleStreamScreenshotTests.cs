using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Captures successive stills of a paced loopback stream (<c>Samples: N</c> at a sample interval) arriving in the TUI,
/// for the user guide's loopback walkthrough; <c>scripts/make_gif.py</c> assembles them into an animation.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class LoopbackSampleStreamScreenshotTests
{
    private const int _intervalMs = 400;
    private const string _prefix = "tui-loopback-stream-";
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(10);

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task PacedSamples_ArriveOneAtATime_StillsAreCaptured()
    {
        var imagesDirectory = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
        Directory.CreateDirectory(imagesDirectory);
        var transport = new DevTerm.Transports.Loopback.LoopbackTransport(Options.Create(new DevTerm.Transports.Loopback.LoopbackTransportOptions { SampleIntervalMs = _intervalMs }));
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        await using var session = new Session(transport, new DevTerm.Core.Presenters.Pipeline([presenter]));
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "loopback", LoopbackSampleIntervalMs = _intervalMs, Presenter = ["ascii"] };

        var dumps = new List<string>();
        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            // The command is awaited by the transport until the last paced line is pushed, so run it unawaited.
            var send = Task.Run(() => session.SendAsync(Encoding.ASCII.GetBytes("Samples: 6\r"), TestContext.CancellationToken), TestContext.CancellationToken);

            foreach (var count in new[] { 1, 2, 3, 4, 5, 6 })
            {
                var seen = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Split('\n').Count(l => l.Contains("[ascii]", StringComparison.Ordinal)) >= count, _waitTimeout);
                Assert.IsTrue(seen, $"Expected {count} sample line(s) to have arrived.");
                dumps.Add(TuiTestRunner.InvokeOnLoop(() =>
                {
                    TuiScreenshot.Save(Path.Combine(imagesDirectory, $"{_prefix}{count}.png"));
                    return TuiTestRunner.DumpBuffer();
                }));
            }

            send.GetAwaiter().GetResult();
        });

        Assert.Contains("[ascii]", dumps[0]);
        Assert.AreNotEqual(dumps[0], dumps[^1], "The last still should show more samples than the first.");
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
