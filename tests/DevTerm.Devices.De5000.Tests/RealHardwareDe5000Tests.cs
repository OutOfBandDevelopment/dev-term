using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Ble;
using DevTerm.Transports.Ble.Windows;
using Microsoft.Extensions.Options;

namespace DevTerm.Devices.De5000.Tests;

/// <summary>
/// Opt-in test against a real DE-5000 over its optical-to-BLE adapter (see
/// docs/design/proposals/de5000-lcr-meter-protocol.md). Same in-process pattern as
/// <c>DevTerm.Devices.ZoomH4n.Tests.RealHardwareZoomH4nTests</c>: constructs a real
/// <see cref="BleTransport"/> directly (via <see cref="WindowsBleAdapterFactory"/>), parameterized
/// entirely via <c>devterm.runsettings</c>.
///
/// Unlike every other real-hardware test in this repo, this one can't fully confirm protocol
/// correctness even on a pass: the adapter's actual GATT profile (whether it really exposes the
/// Nordic UART Service <see cref="BleTransportOptions"/> defaults to, or a custom one) is still
/// unconfirmed - see BACKLOG.md. A pass here proves the connection opens and at least one
/// well-formed 17-byte packet is decoded within the timeout; it does not prove the GATT UUIDs used
/// are the adapter's real ones if this happens to also be the value that a mismatched
/// service/characteristic silently degrades to (it wouldn't - a wrong UUID fails to subscribe rather
/// than returning garbage - but the point stands that this test alone shouldn't be read as a full
/// protocol confirmation).
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Ble)]
[TestCategory(TestCategories.DerEe_De5000)]
[TestClass]
public sealed class RealHardwareDe5000Tests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(35);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    public async Task Connect_AgainstRealDevice_DecodesAtLeastOneFrame()
    {
        var deviceId = GetProperty("RealBleDe5000DeviceId");
        if (string.IsNullOrEmpty(deviceId))
        {
            Assert.Inconclusive("No 'RealBleDe5000DeviceId' - run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        var options = Options.Create(new BleTransportOptions { DeviceId = deviceId });
        var serviceUuid = GetProperty("RealBleDe5000ServiceUuid");
        if (!string.IsNullOrEmpty(serviceUuid))
        {
            options.Value.ServiceUuid = serviceUuid;
        }

        var writeUuid = GetProperty("RealBleDe5000WriteCharacteristicUuid");
        if (!string.IsNullOrEmpty(writeUuid))
        {
            options.Value.WriteCharacteristicUuid = writeUuid;
        }

        var notifyUuid = GetProperty("RealBleDe5000NotifyCharacteristicUuid");
        if (!string.IsNullOrEmpty(notifyUuid))
        {
            options.Value.NotifyCharacteristicUuid = notifyUuid;
        }

        await using var transport = new BleTransport(new WindowsBleAdapterFactory(), options);
        var decoder = new De5000Decoder();
        await using var session = new Session(transport, new Pipeline([decoder]));

        string? firstLine = null;
        session.Output += (_, output) => firstLine ??= output.Text;

        TestContext.WriteLine("Opening connection...");
        await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connection open. Waiting for a decoded frame...");

        var deadline = DateTime.UtcNow + _timeout;
        while (firstLine is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, TestContext.CancellationToken);
        }

        TestContext.WriteLine("Closing connection...");
        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connection closed.");

        Assert.IsNotNull(firstLine, "No frame was decoded within the timeout - check the GATT service/characteristic UUIDs match the adapter's real profile.");
        TestContext.WriteLine($"Decoded: {firstLine}");
    }
}
