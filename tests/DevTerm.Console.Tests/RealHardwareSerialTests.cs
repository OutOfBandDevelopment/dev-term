using System.IO.Ports;
using System.Text;
using System.Threading.Channels;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Serial;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Opt-in tests against real serial-attached SCPI bench instruments (an HP/Agilent/Keysight 34401A
/// multimeter and two Korad KA3005P/KA6003P power supplies). Drives a real <see cref="SerialTransport"/>
/// directly (the same one <c>AddDevTermFrontEnd</c> wires up via DI, built here without a
/// container), the same in-process pattern as <c>DevTerm.Devices.K8055.Tests.RealHardwareK8055Tests</c>
/// / <c>DevTerm.Devices.Busylight.Tests.RealHardwareBusylightTests</c> — no reason to spawn/pipe a
/// whole <c>DevTerm.Console.dll</c> child process and scrape its stdout when the transport itself is
/// directly constructible; that also sidesteps <see cref="DevTerm.Presenters.Text.AsciiPresenter"/>'s
/// CR/LF-terminator-based line buffering, which would never flush a Korad reply (no terminator at
/// all — see <see cref="RawPresenter"/>, shared with <c>RealHardwareUsbtmcTests</c>).
///
/// Parameterized entirely via <c>.runsettings</c> (see <c>devterm.runsettings</c>) — COM port
/// assignment is reassigned by Windows whenever a USB-serial adapter is replugged into a different
/// physical port, so hardcoding one would break the moment a cable moves, which is exactly why the
/// user asked for this to be parameterized rather than literal. Each test preflights its COM port's
/// existence via <see cref="RealDeviceReachability.IsSerialPortAvailable"/> and reports
/// <see cref="Assert.Inconclusive(string)"/> (never a failure or a hang) when the port isn't
/// enumerated by the OS at all — a device that's simply powered off but still cabled/enumerated
/// (a real, common bench state, see docs/changes/2026-09-23.md's KA6003P notes) is instead caught by
/// the query itself timing out, which also reports Inconclusive rather than failing the build.
///
/// Per the user's explicit ask, this only exercises identity plus a couple of core, read-only
/// functions per device (not every SCPI command each profile defines) and treats "no fault, no
/// timeout" as passing — an *ExpectedIdnReply .runsettings parameter is asserted with
/// <see cref="Assert.Contains(string, string)"/> only when it's non-blank, so an unverified guess
/// is never baked in as a hard assertion (see devterm.runsettings' comments for which devices'
/// identity replies are actually confirmed against real hardware so far).
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Serial)]
[TestCategory(TestCategories.Scpi)]
[TestClass]
public sealed class RealHardwareSerialTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    /// <summary>
    /// HP/Agilent/Keysight 34401A: 9600 8N2, no handshake, LF terminator (real-hardware confirmed,
    /// see docs/changes/2026-09-23.md). <c>SYSTem:REMote</c> must be sent before any query or the
    /// instrument replies with SCPI error +550 — it sends no reply of its own, so it's not one of
    /// the "expects a reply" steps below. <c>MEAS:VOLT:DC?</c> is the one core measurement function
    /// exercised beyond identity, per the user's "a few core functions, not every operation" ask.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Hp_34401a)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstHp34401a_AnswersIdentityAndMeasuresVoltage() =>
        RunAsync(
            "RealSerialHp34401a",
            "\n",
            [
                ("SYSTem:REMote", false),
                ("*IDN?", true),
                ("MEAS:VOLT:DC?", true),
            ]);

    /// <summary>
    /// Korad KA3005P: 9600 8N1, no handshake, and — unlike every other real device this suite talks
    /// to — no line terminator at all on either side of the wire (a stray CR/LF desyncs its
    /// fixed-width parser for the rest of the connection, confirmed against real hardware; see
    /// docs/changes/2026-09-23.md). <c>VOUT1?</c>/<c>IOUT1?</c> (read-only output voltage/current
    /// queries, safe regardless of what's connected) are the core functions exercised beyond
    /// identity.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Korad_Ka3005p)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstKoradKa3005p_AnswersIdentityAndQueriesOutput() =>
        RunAsync(
            "RealSerialKa3005p",
            string.Empty,
            [
                ("*IDN?", true),
                ("VOUT1?", true),
                ("IOUT1?", true),
            ]);

    /// <summary>Korad KA6003P: same protocol/settings notes as the KA3005P above.</summary>
    [TestMethod]
    [TestCategory(TestCategories.Korad_Ka6003p)]
    [TestCategory(TestCategories.Hardware)]
    public Task CliMode_AgainstKoradKa6003p_AnswersIdentityAndQueriesOutput() =>
        RunAsync(
            "RealSerialKa6003p",
            string.Empty,
            [
                ("*IDN?", true),
                ("VOUT1?", true),
                ("IOUT1?", true),
            ]);

    private async Task RunAsync(string parameterPrefix, string commandTerminator, (string Command, bool ExpectsReply)[] steps)
    {
        var port = GetProperty($"{parameterPrefix}Port");
        var baud = GetProperty($"{parameterPrefix}Baud");
        var dataBits = GetProperty($"{parameterPrefix}DataBits");
        var parity = GetProperty($"{parameterPrefix}Parity");
        var stopBits = GetProperty($"{parameterPrefix}StopBits");
        var handshake = GetProperty($"{parameterPrefix}Handshake");
        var expectedIdnReplySubstring = GetProperty($"{parameterPrefix}ExpectedIdnReply");

        TestContext.WriteLine($"Connecting: serial {port} {baud} {dataBits}{parity}{stopBits}, handshake={handshake}");
        TestContext.WriteLine($"Expected IDN reply substring: {expectedIdnReplySubstring}");

        if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(baud) || string.IsNullOrEmpty(dataBits)
            || string.IsNullOrEmpty(parity) || string.IsNullOrEmpty(stopBits) || string.IsNullOrEmpty(handshake))
        {
            Assert.Inconclusive($"No '{parameterPrefix}Port'/Baud/DataBits/Parity/StopBits/Handshake — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsSerialPortAvailable(port))
        {
            Assert.Inconclusive($"'{port}' is not currently enumerated by the OS — is the device plugged in/its USB-serial adapter connected?");
            return;
        }

        var options = Options.Create(new SerialTransportOptions
        {
            PortName = port,
            BaudRate = int.Parse(baud),
            DataBits = int.Parse(dataBits),
            Parity = Enum.Parse<Parity>(parity),
            StopBits = Enum.Parse<StopBits>(stopBits),
            Handshake = Enum.Parse<Handshake>(handshake),
        });

        await using var transport = new SerialTransport(new SystemSerialPortFactory(), options);
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

            var reply = await replies.Reader.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
            TestContext.WriteLine($"Received: {reply}");
            Assert.IsFalse(string.IsNullOrEmpty(reply), $"Expected a non-empty reply to '{command}' from the real device on {port} before the timeout elapsed.");

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
