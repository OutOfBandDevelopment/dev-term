using System.IO.Ports;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Serial;
using Microsoft.Extensions.Options;

namespace DevTerm.Devices.ZoomH4n.Tests;

/// <summary>
/// Opt-in test against a real Zoom H4n over its RC04/RC2 remote port (a plain serial connection via
/// the h4n2rs485 adapter — 2400 8-N-1, no handshake — see
/// docs/design/proposals/zoom-h4n-remote-protocol.md). Same in-process pattern as
/// <c>DevTerm.Console.Tests.RealHardwareSerialTests</c>: constructs a real <see cref="SerialTransport"/>
/// directly, parameterized entirely via <c>devterm.runsettings</c> so a COM port reassignment
/// (Windows reassigns these whenever a USB-serial adapter is replugged) doesn't require a code change.
///
/// Per the "no fault, no timeout" bar used by every other real-hardware test in this repo: this
/// only proves the init handshake completes and a button command sends without the device rejecting
/// it or the connection faulting - not every one of the 12 buttons, and not the exact wire-level
/// reply (the H4n's remote port is send-oriented; whether it echoes anything back for a given
/// physical unit hasn't been confirmed on real hardware yet).
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Serial)]
[TestCategory(TestCategories.Zoom_H4n)]
[TestClass]
public sealed class RealHardwareZoomH4nTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(35);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    public async Task InvokeAsync_AgainstRealDevice_CompletesHandshakeAndSendsAButtonCommand()
    {
        var port = GetProperty("RealSerialZoomH4nPort");
        if (string.IsNullOrEmpty(port))
        {
            Assert.Inconclusive("No 'RealSerialZoomH4nPort' - run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsSerialPortAvailable(port))
        {
            Assert.Inconclusive($"'{port}' is not currently enumerated by the OS — is the h4n2rs485 adapter plugged in?");
            return;
        }

        var options = Options.Create(new SerialTransportOptions
        {
            PortName = port,
            BaudRate = 2400,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            Handshake = Handshake.None,
        });

        await using var transport = new SerialTransport(new SystemSerialPortFactory(), options);
        var decoder = new ZoomH4nDecoder();
        await using var session = new Session(transport, new Pipeline([decoder]));

        TestContext.WriteLine("Opening connection...");
        await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connection open.");

        var surface = new ZoomH4nControlSurface(session);

        TestContext.WriteLine("Sending 'mic' (runs the init handshake first if needed)...");
        await surface.InvokeAsync("mic", null, TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Command sent without a fault or timeout.");

        TestContext.WriteLine("Closing connection...");
        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connection closed.");
    }
}
