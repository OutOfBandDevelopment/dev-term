using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Hid;
using Microsoft.Extensions.Options;

namespace DevTerm.Devices.Nmea.Tests;

/// <summary>
/// Opt-in test against a real DeLorme Earthmate GPS BT-20 (USB HID, VID 0x1163/PID 0x0200) — the one
/// physical unit this repo currently confirms speaks the generic <see cref="NmeaGpsDecoder"/>. Same
/// in-process pattern as <c>DevTerm.Devices.K8055.Tests.RealHardwareK8055Tests</c>: constructs a real
/// <see cref="HidTransport"/> directly rather than through DI, since a receive-only device has no
/// command to invoke — this only proves the connection opens and at least one line is decoded.
/// Preflights via <see cref="RealDeviceReachability.IsHidDeviceAvailable"/> and reports
/// <see cref="Assert.Inconclusive(string)"/>, never a failure, when the device isn't currently
/// enumerated (no unit was available this session — see docs/design/proposals/nmea-gps-protocol.md's
/// Status section for what's confirmed vs. assumed).
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Hid)]
[TestCategory(TestCategories.Delorme_EarthmateBt20)]
[TestCategory(TestCategories.Hardware)]
[TestClass]
public sealed class RealHardwareEarthmateBt20Tests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(35);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    [TestMethod]
    public async Task Connect_AgainstRealDevice_DecodesAtLeastOneLine()
    {
        var vendorIdText = GetProperty("RealHidEarthmateBt20-VendorId");
        var productIdText = GetProperty("RealHidEarthmateBt20-ProductId");
        var devicePath = GetProperty("RealHidEarthmateBt20-DevicePath");

        if (string.IsNullOrEmpty(vendorIdText) || string.IsNullOrEmpty(productIdText)
            || !int.TryParse(vendorIdText, out var vendorId) || !int.TryParse(productIdText, out var productId))
        {
            Assert.Inconclusive("No 'RealHidEarthmateBt20-VendorId'/'RealHidEarthmateBt20-ProductId' — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsHidDeviceAvailable(vendorId, productId, devicePath))
        {
            Assert.Inconclusive($"No Earthmate BT-20 HID device (VendorId {vendorId}, ProductId {productId}) is currently enumerated — is it plugged in?");
            return;
        }

        var options = Options.Create(new HidTransportOptions
        {
            VendorId = vendorId,
            ProductId = productId,
            DevicePath = string.IsNullOrEmpty(devicePath) ? null : devicePath,
        });

        await using var transport = new HidTransport(new SystemHidDeviceFactory(), options);
        var decoder = new NmeaGpsDecoder();
        await using var session = new Session(transport, new Pipeline([decoder]));

        string? firstLine = null;
        session.Output += (_, output) => firstLine ??= output.Text;

        TestContext.WriteLine("Opening connection...");
        await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connection open. Waiting for a decoded NMEA sentence...");

        var deadline = DateTime.UtcNow + _timeout;
        while (firstLine is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, TestContext.CancellationToken);
        }

        TestContext.WriteLine("Closing connection...");
        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connection closed.");

        Assert.IsNotNull(firstLine, "No line was decoded within the timeout.");
        TestContext.WriteLine($"Decoded: {firstLine}");
    }
}
