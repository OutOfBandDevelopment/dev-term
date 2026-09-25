using System.Text;
using System.Threading.Channels;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Usbtmc;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Opt-in tests against real USBTMC-attached Rigol SCPI bench instruments (a DM3058E multimeter, a
/// DS1102E oscilloscope, and a DG1062Z function generator) — same in-process pattern and
/// "identity plus a couple of core, read-only functions" scope as <see cref="RealHardwareSerialTests"/>,
/// using a real <see cref="UsbtmcTransport"/> instead of <see cref="DevTerm.Transports.Serial.SerialTransport"/>,
/// and the same shared <see cref="RawPresenter"/> to sidestep <see cref="DevTerm.Presenters.Text.AsciiPresenter"/>'s
/// line-terminator buffering (the DS1102E's replies carry no CR/LF at all).
///
/// VendorId/ProductId/SerialNumber defaults in <c>devterm.runsettings</c> come from a real bench
/// <c>--listusbtmcdevices true</c> enumeration (see docs/test/2026-09-24-07-16-34.md), not guesses.
/// A Rigol DG1022 function generator also on that bench enumerates under the exact same VID:PID as
/// the DS1102E (0x1AB1:0x0588) — RealUsbtmcDs1102eSerialNumber/RealUsbtmcDg1022SerialNumber
/// disambiguate them (see rigol-ds1102e.json's Notes and BACKLOG.md's USBTMC entry). The DM3058E has
/// its own previously-parked USBTMC bulk-IN stall (docs/changes/2026-09-23.md/BACKLOG.md) — if that
/// regresses, this test times out rather than silently passing, which is the desired behavior for a
/// regression check.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Usbtmc)]
[TestCategory(TestCategories.Scpi)]
[TestClass]
[DoNotParallelize]
public sealed class RealHardwareUsbtmcTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    /// <summary>
    /// Rigol DM3058E: identity plus a system-version query and one read-only DC voltage measurement
    /// — safe regardless of what (if anything) is wired to the input terminals.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Rigol_Dm3058e)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstRigolDm3058e_AnswersIdentityAndMeasuresVoltage() =>
        RunAsync(
            "RealUsbtmcDm3058e",
            "\n",
            [
                ("*IDN?", true),
                ("SYSTem:VERSion?", true),
                (":MEASure:VOLTage:DC?", true),
            ]);

    /// <summary>
    /// Rigol DS1102E: identity plus two read-only CH1 measurement queries (peak-to-peak voltage,
    /// frequency) — safe regardless of the probe's actual connection state.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Rigol_Ds1102e)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstRigolDs1102e_AnswersIdentityAndMeasuresChannel1() =>
        RunAsync(
            "RealUsbtmcDs1102e",
            string.Empty,
            [
                ("*IDN?", true),
                (":MEAS:VPP? CHAN1", true),
                (":MEAS:FREQ? CHAN1", true),
            ]);

    /// <summary>
    /// Rigol DG1062Z: identity plus two read-only CH1 setup queries (current waveform, frequency) —
    /// neither mutates output state.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Rigol_Dg1062z)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstRigolDg1062z_AnswersIdentityAndQueriesChannel1() =>
        RunAsync(
            "RealUsbtmcDg1062z",
            "\n",
            [
                ("*IDN?", true),
                ("SOURce1:APPLy?", true),
                ("SOURce1:FREQuency?", true),
            ]);

    /// <summary>
    /// Rigol DG1022: identity plus read-only CH1 output-status and frequency queries — neither
    /// mutates output state. Shares its exact VID:PID with the DS1102E (see the class doc comment
    /// above), so RealUsbtmcDg1022SerialNumber must disambiguate it.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Rigol_Dg1022)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstRigolDg1022_AnswersIdentityAndQueriesChannel1() =>
        RunAsync(
            "RealUsbtmcDg1022",
            "\n",
            [
                ("*IDN?", true),
                ("OUTPut?", true),
                ("FREQuency?", true),
            ]);

    private async Task RunAsync(string parameterPrefix, string commandTerminator, (string Command, bool ExpectsReply)[] steps)
    {
        var vendorIdText = GetProperty($"{parameterPrefix}VendorId");
        var productIdText = GetProperty($"{parameterPrefix}ProductId");
        var serialNumber = GetProperty($"{parameterPrefix}SerialNumber");
        var expectedIdnReplySubstring = GetProperty($"{parameterPrefix}ExpectedIdnReply");

        TestContext.WriteLine($"VendorId/ProductId/SerialNumber: {vendorIdText}/{productIdText}/{serialNumber}");
        TestContext.WriteLine($"Expected IDN reply substring: {expectedIdnReplySubstring}");

        if (string.IsNullOrEmpty(vendorIdText) || string.IsNullOrEmpty(productIdText)
            || !int.TryParse(vendorIdText, out var vendorId) || !int.TryParse(productIdText, out var productId))
        {
            Assert.Inconclusive($"No '{parameterPrefix}VendorId'/'{parameterPrefix}ProductId' — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsUsbtmcDeviceAvailable(vendorId, productId, serialNumber))
        {
            Assert.Inconclusive($"No USBTMC device (VendorId {vendorId}, ProductId {productId}, SerialNumber '{serialNumber}') is currently enumerated — is it plugged in and powered on?");
            return;
        }

        var options = Options.Create(new UsbtmcTransportOptions
        {
            VendorId = vendorId,
            ProductId = productId,
            SerialNumber = string.IsNullOrEmpty(serialNumber) ? null : serialNumber,
        });

        await using var transport = new UsbtmcTransport(new SystemUsbtmcDeviceFactory(), options);
        var replies = Channel.CreateUnbounded<string>();
        await using var session = new Session(transport, new Pipeline([new RawPresenter()]));
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

            var reply = await ReplyCollector.ReadReplyAsync(replies.Reader, _timeout, ReplyCollector.DefaultQuietGap, TestContext.CancellationToken);
            TestContext.WriteLine($"Received: {reply}");
            Assert.IsFalse(string.IsNullOrEmpty(reply), $"Expected a non-empty reply to '{command}' from the real device before the timeout elapsed.");

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
