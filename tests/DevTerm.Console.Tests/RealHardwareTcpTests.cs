using System.Text;
using System.Threading.Channels;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Opt-in tests against real bench instruments reachable over TCP via a serial-to-Ethernet bridge —
/// two Tektronix 2230 oscilloscopes (pre-SCPI, answer <c>ID?</c>) and a Tektronix TDS2024 (SCPI,
/// answers <c>*IDN?</c> instead) — also covered more shallowly by <see cref="RealHardwareCliTests"/>'s
/// generic identity-only check across all three. Same in-process "identity plus a couple of core,
/// read-only functions" pattern as <see cref="RealHardwareSerialTests"/>/<see cref="RealHardwareUsbtmcTests"/>,
/// using a real <see cref="TcpTransport"/> directly instead of spawning <c>DevTerm.Console.dll</c> as
/// a child process.
///
/// Unlike those two siblings, this uses <see cref="AsciiPresenter"/> (with each device profile's own
/// terminator — LF for the TDS2024, CR for the 2230s; see <c>tektronix-tds2024.json</c>/
/// <c>tektronix-2230.json</c>'s <c>Terminator</c>), not the shared terminatorless
/// <see cref="RawPresenter"/>: all three are real line-terminated devices, and <c>RawPresenter</c>
/// reading exactly one <see cref="Session.Output"/> item per command is only trustworthy for a
/// terminatorless reply — a terminated reply can arrive over the wire in multiple chunks, each firing
/// <c>Session.Output</c> separately, which would otherwise misattribute a later chunk to the next
/// command's reply (see BACKLOG.md's note on the HP 34401A hitting exactly this with the shared
/// harness, confirmed docs/test/2026-09-25-15-02-44.md).
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Tcp)]
[TestCategory(TestCategories.Scpi)]
[TestClass]
public sealed class RealHardwareTcpTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    /// <summary>
    /// Tektronix TDS2024: identity plus two read-only channel-settings queries (CH1, CH2) — neither
    /// mutates acquisition/trigger state, safe regardless of what's connected to the input channels.
    ///
    /// NOT any <c>TRIGger:...?</c> query — real-hardware confirmed that every <c>TRIGger</c>-family
    /// query tried (<c>TRIGger:MAIn:FREQuency?</c>, then <c>TRIGger:STATE?</c>) hangs the full step
    /// timeout on this specific unit (docs/test/2026-09-25-18-57-22.md), even though both are real
    /// commands in tektronix-tds2024.json and every non-Trigger query tried answered normally
    /// (including as the 3rd command in the sequence, ruling out a simple "3rd command" positional
    /// issue). Root cause not identified — tracked in BACKLOG.md rather than investigated further
    /// here; avoid the whole <c>TRIGger</c> command family against this device until it's understood.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Tektronix_Tds2024)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstTektronixTds2024_AnswersIdentityAndQueriesChannel1() =>
        RunAsync(
            "RealTcpDeviceHost3",
            "\n",
            [
                ("*IDN?", true),
                ("CH1?", true),
                ("CH2?", true),
            ]);

    /// <summary>
    /// Tektronix 2230 (.107, <c>RealTcpDeviceHost1</c>): pre-SCPI identity plus a read-only CH1
    /// settings query — neither mutates acquisition/trigger state.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Tektronix_2230)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstTektronix2230Host1_AnswersIdentityAndQueriesChannel1() =>
        RunAsync(
            "RealTcpDeviceHost1",
            "\r",
            [
                ("ID?", true),
                ("CH1?", true),
                ("HORizontal?", true),
            ]);

    /// <summary>
    /// Tektronix 2230 (.108, <c>RealTcpDeviceHost2</c>): same protocol/settings notes as Host1 above
    /// — a second, physically distinct unit on this bench.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Tektronix_2230)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstTektronix2230Host2_AnswersIdentityAndQueriesChannel1() =>
        RunAsync(
            "RealTcpDeviceHost2",
            "\r",
            [
                ("ID?", true),
                ("CH1?", true),
                ("HORizontal?", true),
            ]);

    private async Task RunAsync(string parameterPrefix, string commandTerminator, (string Command, bool ExpectsReply)[] steps)
    {
        var host = GetProperty(parameterPrefix);
        var portText = GetProperty("RealTcpDevicePort");
        var expectedIdnReplySubstring = GetProperty($"{parameterPrefix}ExpectedReply");

        TestContext.WriteLine($"Connecting: tcp {host}:{portText}");
        TestContext.WriteLine($"Expected IDN reply substring: {expectedIdnReplySubstring}");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(portText) || !int.TryParse(portText, out var port))
        {
            Assert.Inconclusive($"No '{parameterPrefix}'/'RealTcpDevicePort' — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!await RealDeviceReachability.IsTcpReachableAsync(host, port, TestContext.CancellationToken))
        {
            Assert.Inconclusive($"Real device at {host}:{port} is not reachable (TCP connect attempt timed out/refused within {RealDeviceReachability.DefaultTimeout.TotalSeconds}s) — is it powered on and networked?");
            return;
        }

        var options = Options.Create(new TcpTransportOptions
        {
            Mode = TcpTransportMode.Client,
            Host = host,
            Port = port,
        });

        await using var transport = new TcpTransport(new SystemTcpConnectionSource(), options);
        var replies = Channel.CreateUnbounded<string>();
        await using var session = new Session(transport, new Pipeline([new AsciiPresenter(Options.Create(new AsciiPresenterOptions()))]));
        session.Output += (_, output) => replies.Writer.TryWrite(output.Text);

        TestContext.WriteLine("Opening connection...");
        await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connection open.");

        var isFirstReply = true;
        foreach (var (command, expectsReply) in steps)
        {
            TestContext.WriteLine($"Sending: {command}");
            var bytes = Encoding.ASCII.GetBytes(command + commandTerminator);
            await session.SendAsync(bytes, TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

            if (!expectsReply)
            {
                TestContext.WriteLine("  (no reply expected)");
                continue;
            }

            var reply = await replies.Reader.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
            TestContext.WriteLine($"Received: {reply}");
            Assert.IsFalse(string.IsNullOrEmpty(reply), $"Expected a non-empty reply to '{command}' from the real device at {host}:{port} before the timeout elapsed.");

            if (isFirstReply && !string.IsNullOrEmpty(expectedIdnReplySubstring))
            {
                Assert.Contains(expectedIdnReplySubstring, reply);
            }

            isFirstReply = false;
        }

        TestContext.WriteLine("Closing connection...");
        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connection closed.");
    }
}
