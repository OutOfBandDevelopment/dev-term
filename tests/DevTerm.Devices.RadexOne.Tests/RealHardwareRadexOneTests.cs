using System.IO.Ports;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Serial;
using Microsoft.Extensions.Options;

using DevTerm.DeviceManifests;

namespace DevTerm.Devices.RadexOne.Tests;

/// <summary>
/// Opt-in test against a real Radex One over its virtual COM port (9600 8-N-1, no handshake — real-
/// hardware confirmed 2026-09-26 on COM8, correcting an earlier, unverified "2400 baud" claim; see
/// docs/bugs/resolved/061-radexone-wrong-baud-rate.md and docs/design/proposals/radex-one-protocol.md).
/// Same in-process pattern as <c>DevTerm.Devices.ZoomH4n.Tests.RealHardwareZoomH4nTests</c>: constructs
/// a real <see cref="SerialTransport"/> directly, parameterized entirely via
/// <c>devterm.runsettings</c> so a COM port reassignment doesn't require a code change.
///
/// <para>An earlier draft of this module was built on a wrong "confirmed directly... USB HID device"
/// assumption (see the proposal doc's "Device" section) and sent every request wrapped in a fake HID
/// report — that would have corrupted every byte on a real serial connection. This test, along with
/// <see cref="RadexOneDecoder"/> and <see cref="RadexOneControlSurface"/>, was corrected to plain
/// serial once a real device turned up enumerated as a COM port instead of a HID device.</para>
///
/// Sends a Read Data query and waits for a reply to reach <see cref="RadexOneDecoder"/> via
/// <see cref="Session.Output"/> — the "no fault, no timeout, at least one decoded reply" bar used by
/// every other real-hardware test in this repo.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Serial)]
[TestCategory(TestCategories.Radex_One)]
[DoNotParallelize]
[TestClass]
public sealed class RealHardwareRadexOneTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    public async Task RealDevice_ReadData_ReceivesADecodedReply()
    {
        var port = GetProperty("RealSerialRadexOnePort");
        if (string.IsNullOrEmpty(port))
        {
            Assert.Inconclusive("No 'RealSerialRadexOnePort' - run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsSerialPortAvailable(port))
        {
            Assert.Inconclusive($"'{port}' is not currently enumerated by the OS — is the Radex One plugged in?");
            return;
        }

        var options = Options.Create(new SerialTransportOptions
        {
            PortName = port,
            BaudRate = 9600,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            Handshake = Handshake.None,
        });

        await using var transport = new SerialTransport(new SystemSerialPortFactory(), options);
        await using var session = new Session(transport, new Pipeline([new RadexOneDecoder()]));

        string? received = null;
        session.Output += (_, output) => received = output.Text;

        TestContext.WriteLine($"Connecting to {port}...");
        await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connected.");

        var controlSurface = new RadexOneControlSurface(session);
        TestContext.WriteLine("Sending: readData");
        await controlSurface.InvokeAsync("readData", null, TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        var deadline = DateTime.UtcNow + _timeout;
        while (received is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.CancellationToken);
        }

        TestContext.WriteLine($"Received: {received ?? "(nothing)"}");
        Assert.IsNotNull(received, "No reply received from the real device.");

        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Closed.");
    }

    /// <summary>The imported <c>radexone-read-data-reply.ksy</c> layout, run live through <see cref="ManifestFramePresenter"/> against the real device's Read Data reply.</summary>
    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    public async Task RealDevice_ReadData_PublishesFieldsFromTheImportedKsyFrame()
    {
        var port = GetProperty("RealSerialRadexOnePort");
        if (string.IsNullOrEmpty(port))
        {
            Assert.Inconclusive("No 'RealSerialRadexOnePort' - run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsSerialPortAvailable(port))
        {
            Assert.Inconclusive($"'{port}' is not currently enumerated by the OS — is the Radex One plugged in?");
            return;
        }

        var ksy = KsyImporter.Import(File.ReadAllText(Path.Combine(DevicesDirectory(), "radexone", "radexone-read-data-reply.ksy")));
        Assert.IsNotNull(ksy.Schema, string.Join("; ", ksy.Warnings));
        var presenter = new ManifestFramePresenter(ksy.Schema);
        IReadOnlyDictionary<string, string>? values = null;
        presenter.ValuesChanged += (_, published) => values = published;

        var options = Options.Create(new SerialTransportOptions
        {
            PortName = port,
            BaudRate = 9600,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            Handshake = Handshake.None,
        });

        await using var transport = new SerialTransport(new SystemSerialPortFactory(), options);
        await using var session = new Session(transport, new Pipeline([presenter]));

        await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        await new RadexOneControlSurface(session).InvokeAsync("readData", null, TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        var deadline = DateTime.UtcNow + _timeout;
        while (values is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.CancellationToken);
        }

        TestContext.WriteLine($"Published: {(values is null ? "(nothing)" : string.Join(", ", values.Select(pair => $"{pair.Key}={pair.Value}")))}");
        Assert.IsNotNull(values, "The frame presenter published nothing from the real device's reply.");
        Assert.AreEqual("2048", values["extension.command_code"]);
        Assert.IsTrue(int.TryParse(values["extension.cpm"], out _));

        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
    }

    private static string DevicesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Could not find DevTerm.slnx."), "docs", "devices");
    }
}
