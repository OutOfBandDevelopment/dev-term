using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Exercises <see cref="LoopbackTransport"/> itself through a real <see cref="Session"/>/
/// <see cref="AsciiPresenter"/> pair — the same shape <c>TuiMode</c>/<c>MainWindow</c> use — to
/// prove the scripted request/response and multi-line "event stream" behavior other tests can build
/// on actually works as designed. No real transport I/O anywhere, so this is <c>UNIT</c>, not
/// <c>INTEGRATION</c> (contrast <c>TuiModeSwitchProfileTests</c>, which needs a real TCP loopback
/// socket because it goes through <c>DevTermSessionBuilder</c>).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class LoopbackTransportTests
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    private static (Session Session, List<string> Lines) CreateSession(LoopbackTransport transport)
    {
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        var lines = new List<string>();
        session.Output += (_, output) =>
        {
            lock (lines)
            {
                lines.Add(output.Text);
            }
        };
        return (session, lines);
    }

    private static async Task<IReadOnlyList<string>> WaitForLinesAsync(List<string> lines, int count)
    {
        var deadline = DateTime.UtcNow + _waitTimeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (lines)
            {
                if (lines.Count >= count)
                {
                    return lines.ToArray();
                }
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {count} line(s); got {lines.Count}.");
        return lines; // Unreachable - Assert.Fail throws.
    }

    [TestMethod]
    public async Task LiteralRule_RespondsWithItsFixedLine()
    {
        var (session, lines) = CreateSession(new LoopbackTransport());
        await session.OpenAsync(TestContext.CancellationToken);

        await session.SendAsync(Encoding.ASCII.GetBytes("hello\r\n"), TestContext.CancellationToken);

        var received = await WaitForLinesAsync(lines, 1);
        Assert.AreEqual("From Loopback test", received[0]);

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task SendStream_RespondsWithADeterministicAsciiRun()
    {
        var (session, lines) = CreateSession(new LoopbackTransport());
        await session.OpenAsync(TestContext.CancellationToken);

        await session.SendAsync(Encoding.ASCII.GetBytes("Send Stream: 30, ascii\r\n"), TestContext.CancellationToken);

        var received = await WaitForLinesAsync(lines, 1);
        Assert.AreEqual("ABCDEFGHIJKLMNOPQRSTUVWXYZABCD", received[0]);

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task SendEvents_RespondsWithOneLinePerEvent()
    {
        var (session, lines) = CreateSession(new LoopbackTransport());
        await session.OpenAsync(TestContext.CancellationToken);

        await session.SendAsync(Encoding.ASCII.GetBytes("Send Events: 10\r\n"), TestContext.CancellationToken);

        var received = await WaitForLinesAsync(lines, 10);
        Assert.AreSequenceEqual([.. Enumerable.Range(1, 10).Select(i => $"Event {i}")], [.. received]);

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task UnrecognizedCommand_RespondsWithAVisibleMarker_RatherThanSilence()
    {
        var (session, lines) = CreateSession(new LoopbackTransport());
        await session.OpenAsync(TestContext.CancellationToken);

        await session.SendAsync(Encoding.ASCII.GetBytes("not a real command\r\n"), TestContext.CancellationToken);

        var received = await WaitForLinesAsync(lines, 1);
        Assert.Contains("not a real command", received[0]);

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task CustomRules_OverrideTheDefaultScript()
    {
        var (session, lines) = CreateSession(new LoopbackTransport([LoopbackRule.Literal("ping", "pong")]));
        await session.OpenAsync(TestContext.CancellationToken);

        await session.SendAsync(Encoding.ASCII.GetBytes("hello\r\n"), TestContext.CancellationToken);
        await session.SendAsync(Encoding.ASCII.GetBytes("ping\r\n"), TestContext.CancellationToken);

        var received = await WaitForLinesAsync(lines, 2);
        Assert.Contains("Unrecognized", received[0]);
        Assert.AreEqual("pong", received[1]);

        await session.CloseAsync(TestContext.CancellationToken);
    }

    public TestContext TestContext { get; set; }
}
