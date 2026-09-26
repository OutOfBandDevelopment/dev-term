using System.Net;
using System.Net.Sockets;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Logging;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>The TUI main window's File &gt; Start Logging... / Stop Logging wiring (after the path prompt) and <c>--log</c>.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
[DoNotParallelize]
public sealed class TuiLoggingTests
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(15);

    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"devterm-tui-logging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    private static (Session Session, FakeTransport Transport, IPresenter Presenter) CreateSession()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        return (new Session(transport, new Pipeline([presenter])), transport, presenter);
    }

    private static string Shape(SessionLog log) => string.Join(",", log.Records.Select(r => r.Kind.ToString().ToLowerInvariant()));

    [TestMethod]
    public async Task StartLogging_FlipsTheMenuItem_ShowsRecOnTheStatusLine_AndRecordsTheSession()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] };
        var path = Path.Combine(_directory, "tui.jsonl");

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
        {
            Assert.AreEqual(TuiLogging.StartTitle, parts.Logging.MenuItem.Title);
            Assert.DoesNotContain("REC", parts.StatusLabel.Text);

            Assert.IsTrue(parts.Logging.Start(path));

            Assert.AreEqual(TuiLogging.StopTitle, parts.Logging.MenuItem.Title);
            Assert.Contains("● REC tui.jsonl", parts.StatusLabel.Text);
            session.SendAsync("ID?\r"u8.ToArray(), TestContext.CancellationToken).GetAwaiter().GetResult();

            parts.Logging.Stop();

            Assert.AreEqual(TuiLogging.StartTitle, parts.Logging.MenuItem.Title);
            Assert.DoesNotContain("REC", parts.StatusLabel.Text);
            Assert.IsNull(parts.Logging.Logger());
        });

        await session.CloseAsync(TestContext.CancellationToken);

        var log = SessionLog.Load(path);
        Assert.AreEqual("session,tx", Shape(log), "Stopping ends the log - the later close isn't in it.");
        Assert.AreEqual("open", log.Records[0].State);
        Assert.AreEqual("tcp://192.168.0.107:23", log.Header.Connection);
        Assert.AreEqual("ascii", log.Header.Parser);
        StringAssert.EndsWith(log.Header.Application, "(tui)");
    }

    [TestMethod]
    public async Task StartLogging_ToAPathThatCannotBeCreated_StaysStopped()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var blocker = Path.Combine(_directory, "a-file");
        File.WriteAllText(blocker, string.Empty);

        TuiTestRunner.RunHeadless(session, presenter, new CliOptions { Transport = "loopback" }, parts =>
        {
            Assert.IsFalse(parts.Logging.Start(Path.Combine(blocker, "x.jsonl")));
            Assert.AreEqual(TuiLogging.StartTitle, parts.Logging.MenuItem.Title);
            Assert.IsNull(parts.Logging.Logger());
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task TheLogOption_StartsLoggingWhenTheWindowOpens()
    {
        var (session, _, presenter) = CreateSession();
        var path = Path.Combine(_directory, "startup.jsonl");

        TuiTestRunner.RunHeadless(session, presenter, new CliOptions { Transport = "loopback", Log = path }, parts =>
        {
            Assert.AreEqual(TuiLogging.StopTitle, parts.Logging.MenuItem.Title);
            Assert.AreEqual(path, parts.Logging.Logger()!.Path);

            // What TuiMode.RunAsync does once the loop ends.
            parts.Logging.Stop();
        });

        await session.DisposeAsync();
        Assert.AreEqual("session", Shape(SessionLog.Load(path)));
    }

    [TestMethod]
    public async Task SwitchingProfile_ContinuesTheSameLog_WithTheNewConnection()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var path = Path.Combine(_directory, "switch.jsonl");

        TuiTestRunner.RunWithLoop(session, presenter, new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Presenter = ["ascii"] }, parts =>
        {
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.Logging.Start(path)));

            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);

            var switched = parts.SwitchProfileAsync(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = port.ToString(), Presenter = ["ascii"] })
                .GetAwaiter().GetResult();
            using var client = acceptTask.GetAwaiter().GetResult();
            Assert.IsTrue(switched);
            Assert.IsTrue(TuiTestRunner.WaitUntilOnLoop(() => parts.StatusLabel.Text.Contains("REC", StringComparison.Ordinal), _waitTimeout));

            TuiTestRunner.InvokeOnLoop(() =>
            {
                parts.Logging.Stop();
                return true;
            });

            var log = SessionLog.Load(path);
            var sessions = log.Records.Where(r => r.Kind == SessionLogRecordKind.Session).ToArray();
            Assert.HasCount(2, sessions);
            Assert.AreEqual($"tcp://127.0.0.1:{port}", sessions[1].Connection);
            Assert.IsTrue(log.Records.Any(r => r.Kind == SessionLogRecordKind.Close), "The old session's close is recorded before the switch.");
            Assert.AreEqual(SessionLogRecordKind.Open, log.Records[^1].Kind, "The new session's connect is recorded after it.");
        });
    }

    public required TestContext TestContext { get; set; }
}
