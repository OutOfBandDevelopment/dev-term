using System.Buffers;
using System.Text;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Configuration.Tests;

/// <summary>The front-end glue for logger mode and playback: paths, headers, the new flags, and the transport-free presenter catalog.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class SessionLoggingTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void DefaultLogPath_IsTimestampedUnderTheLogsDirectory_NamedForTheProfileOrConnection()
    {
        var tcp = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23" };
        var stamp = _now.ToLocalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        Assert.AreEqual(Path.Combine(DevTermUserDataPaths.LogsDirectory, $"{stamp}_tcp_192.168.0.107_23.jsonl"), SessionLogging.DefaultLogPath(tcp, null, _now));
        Assert.AreEqual(Path.Combine(DevTermUserDataPaths.LogsDirectory, $"{stamp}_tek2230.jsonl"), SessionLogging.DefaultLogPath(tcp, "tek2230", _now));
        StringAssert.EndsWith(SessionLogging.DefaultLogPath(new CliOptions { Transport = "serial", Port = "COM3" }, null, _now), "_serial_COM3_9600,8,n,1.jsonl");
    }

    [TestMethod]
    public void ResolveLogPath_TrueMeansTheDefault_AnythingElseIsAPath()
    {
        var options = new CliOptions { Transport = "loopback" };

        Assert.AreEqual(SessionLogging.DefaultLogPath(options, null, _now), SessionLogging.ResolveLogPath("true", options, null, _now));
        Assert.AreEqual(SessionLogging.DefaultLogPath(options, null, _now), SessionLogging.ResolveLogPath("TRUE", options, null, _now));
        Assert.AreEqual(Path.GetFullPath("capture.jsonl"), SessionLogging.ResolveLogPath("capture.jsonl", options, null, _now));
    }

    [TestMethod]
    public void DisplayPath_ShortensTheHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.AreEqual(Path.Combine("~", ".dev-term", "logs", "a.jsonl"), SessionLogging.DisplayPath(Path.Combine(home, ".dev-term", "logs", "a.jsonl")));
        Assert.AreEqual(@"D:\captures\a.jsonl", SessionLogging.DisplayPath(@"D:\captures\a.jsonl"));
    }

    [TestMethod]
    public void HeaderFor_DescribesTheConnection()
    {
        var header = SessionLogging.HeaderFor(new CliOptions { Transport = "tcp", Host = "h", Port = "23", Presenter = ["ascii", "hex"] }, "hex", "bench", "cli", _now);

        Assert.AreEqual("tcp://h:23", header.Connection);
        Assert.AreEqual("bench", header.Profile);
        Assert.AreEqual("tcp", header.Transport);
        CollectionAssert.AreEqual(new[] { "ascii", "hex" }, header.Presenters.ToArray());
        Assert.AreEqual("hex", header.Parser);
        Assert.StartsWith("dev-term ", header.Application);
        StringAssert.EndsWith(header.Application, "(cli)");
    }

    [TestMethod]
    public void TheLogAndPlaybackFlags_Bind_AndAreNeverSavedIntoAProfile()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(["--log", "x.jsonl", "--playback", "y.jsonl", "--playbackspeed", "2.5"])
            .Build();
        var options = new CliOptions();
        DevTermConfiguration.Bind(configuration, options);

        Assert.AreEqual("x.jsonl", options.Log);
        Assert.AreEqual("y.jsonl", options.Playback);
        Assert.AreEqual(2.5, options.PlaybackSpeed);

        var profile = DevTermConfiguration.ToProfileJson(options);
        Assert.DoesNotContain("Log", profile);
        Assert.DoesNotContain("Playback", profile);
    }

    [TestMethod]
    public void PlaybackPresenters_OffersEveryPresenter_ButRegistersNoTransport()
    {
        var presenters = new PlaybackPresenters();

        CollectionAssert.IsSubsetOf(new[] { "ascii", "utf8", "hex", "decimal", "octal", "binary", "scpi" }, presenters.Names.ToArray());

        var services = new ServiceCollection();
        services.AddDevTermPresenters(new CliOptions());
        Assert.IsNull(services.BuildServiceProvider().GetService<ITransport>(), "Playback must never be able to reach a device.");
    }

    [TestMethod]
    public void PlaybackPresenters_BuildsFreshPresenterInstancesEveryTime()
    {
        var presenters = new PlaybackPresenters();
        var first = presenters.CreatePipeline(["ascii"]);
        first.Render(new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes("partial")));

        var second = presenters.CreatePipeline(["ascii"]);
        var rendered = second.Render(new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes("line\r")));

        Assert.AreEqual("line", rendered.Single().Text, "The second pipeline's ASCII presenter didn't inherit the first one's buffered text.");
        Assert.AreNotSame(first.Presenters[0], second.Presenters[0]);
    }
}
