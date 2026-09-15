using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives a real <see cref="TuiMode"/> window — real Terminal.Gui controls, a real <c>Session</c> —
/// via <see cref="TuiTestRunner"/> rather than OS-level UI Automation, mirroring how
/// <c>DevTerm.Wpf.Tests.MainWindowTests</c> drives a real WPF <c>MainWindow</c>. See
/// <see cref="TuiTestRunner"/>'s doc comment for the two run modes this needed and why.
///
/// Runs sequentially, not in parallel with other tests in this assembly: Terminal.Gui's
/// <c>Application</c> state is static/process-global (same class of concern that made
/// <c>DevTerm.Wpf.Tests.MainWindowTests</c> need <c>[DoNotParallelize]</c> for its own, WPF-specific
/// reason).
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class TuiModeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static (Session Session, FakeTransport Transport, IPresenter Presenter) CreateSession()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        return (session, transport, presenter);
    }

    [TestMethod]
    public async Task BuildWindow_RendersTitleAndSendPrompt()
    {
        var (session, _, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23 };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, parts =>
        {
            StringAssert.Contains(parts.Window.Title, "TCP 127.0.0.1:23");
            StringAssert.Contains(parts.Window.Title, "ascii");

            var screen = TuiTestRunner.DumpBuffer();
            StringAssert.Contains(screen, "Send:");
        });

        await session.CloseAsync();
    }

    [TestMethod]
    public async Task TypingAndEnter_SendsLineWithLineEndingToTheTransport()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, LineEnding = LineEnding.Cr };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, _ =>
        {
            TuiTestRunner.TypeText("ID?");
            TuiTestRunner.PressEnter();
        });

        await session.CloseAsync();

        Assert.HasCount(1, transport.WrittenPayloads);
        Assert.AreEqual("ID?\r", Encoding.ASCII.GetString(transport.WrittenPayloads[0]));
    }

    [TestMethod]
    public async Task EmptyInput_Enter_DoesNotWriteToTheTransport()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, LineEnding = LineEnding.None };

        TuiTestRunner.RunHeadless(session, presenter, cliOptions, _ =>
        {
            TuiTestRunner.PressEnter();
        });

        await session.CloseAsync();

        Assert.IsEmpty(transport.WrittenPayloads);
    }

    [TestMethod]
    public async Task IncomingBytes_AppearInOutputThroughTheRealSessionPipeline()
    {
        var (session, transport, presenter) = CreateSession();
        await session.OpenAsync();
        var cliOptions = new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23 };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            transport.PushIncomingAsync(Encoding.ASCII.GetBytes("ID TEK/2230\r")).GetAwaiter().GetResult();

            var appeared = TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Length > 0, WaitTimeout);
            Assert.IsTrue(appeared, "Expected the decoded line to arrive via the real Session pull loop + Application.Invoke.");

            var text = TuiTestRunner.InvokeOnLoop(() => parts.Output.Text);
            StringAssert.Contains(text, "[ascii]");
            StringAssert.Contains(text, "ID TEK/2230");
        });

        await session.CloseAsync();
    }
}
