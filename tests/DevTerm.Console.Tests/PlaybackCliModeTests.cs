using System.Diagnostics;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary><c>--playback</c>: the non-interactive CLI replay, over its real <c>RunAsync</c> with captured stdout/stderr.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class PlaybackCliModeTests
{
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"devterm-cli-playback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    private async Task<(int Exit, string Out, string Err)> RunAsync(CliOptions options, params string[] presenters)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await PlaybackCliMode.RunAsync(options, presenters, stdout, stderr, TimeProvider.System, TestContext.CancellationToken);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [TestMethod]
    public async Task PrintsEveryRecordDecoded_ThroughTheLogsOwnPresenters()
    {
        var (exit, output, errors) = await RunAsync(new CliOptions { Playback = PlaybackModeTests.WriteSampleLog(_directory) });

        Assert.AreEqual(0, exit);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        CollectionAssert.AreEqual(
            new[]
            {
                "00:00.000 [dev-term] Logging tek2230 (tcp://192.168.0.107:23) — closed.",
                "00:00.040 [dev-term] Connected.",
                "00:01.000 [tx] ID?\\r",
                "00:01.250 [ascii] ID TEK/2230,V81.1,VERS:14",
                "00:06.000 [tx] CH1?\\r",
                "00:06.300 [ascii] CH1 VOLTS:1,COUPLING:DC",
                "00:09.000 [error] The device closed the connection.",
            },
            lines);
        Assert.Contains("through 'ascii'", errors);
    }

    [TestMethod]
    public async Task PresenterOverride_ReplacesTheLogsPresenters_AndAnUnknownOneFails()
    {
        var path = PlaybackModeTests.WriteSampleLog(_directory);

        var (exit, output, _) = await RunAsync(new CliOptions { Playback = path }, "hex");
        Assert.AreEqual(0, exit);
        Assert.Contains("00:01.200 [hex] 49442054454B2F323233302C", output);
        Assert.DoesNotContain("[ascii]", output);

        var (badExit, _, errors) = await RunAsync(new CliOptions { Playback = path }, "nope");
        Assert.AreEqual(1, badExit);
        Assert.Contains("Unknown presenter 'nope'", errors);
    }

    [TestMethod]
    public async Task PlaybackSpeed_PacesTheOutput()
    {
        var watch = Stopwatch.StartNew();
        var (exit, _, _) = await RunAsync(new CliOptions { Playback = PlaybackModeTests.WriteSampleLog(_directory), PlaybackSpeed = 30 });

        Assert.AreEqual(0, exit);
        Assert.IsGreaterThanOrEqualTo(280L, watch.ElapsedMilliseconds, "9 s of log at 30x is 300 ms.");
    }

    [TestMethod]
    public async Task AFileThatIsNotALog_ExitsWithAnError()
    {
        var path = Path.Combine(_directory, "junk.jsonl");
        File.WriteAllText(path, "hello\n");

        var (exit, output, errors) = await RunAsync(new CliOptions { Playback = path });

        Assert.AreEqual(1, exit);
        Assert.AreEqual(string.Empty, output);
        Assert.Contains("Not a dev-term session log", errors);
    }

    public required TestContext TestContext { get; set; }
}
