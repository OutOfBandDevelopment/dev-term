using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Hid;
using Microsoft.Extensions.Options;

namespace DevTerm.Devices.K8055.Tests;

/// <summary>
/// Opt-in test against a real Velleman K8055 HID device. Unlike the SCPI serial devices (see
/// <c>DevTerm.Console.Tests.RealHardwareSerialTests</c>), K8055 speaks fixed-length binary HID
/// frames rather than typed ASCII lines, so there's no CLI text loop to drive here — this test
/// constructs a real <see cref="HidTransport"/> (the same one <c>AddDevTermFrontEnd</c> wires up via
/// DI, built directly instead) plus a real <see cref="K8055ControlSurface"/> and invokes one benign
/// command, the same way <c>DevTerm.Wpf.Tests.RealHardwareMainWindowTests</c> drives a real
/// connection without a DI container. <c>resetCounter1</c> is the safest real action available
/// (K8055ControlSurface.cs): a fixed, self-contained frame that doesn't depend on or mutate any
/// other visible output state. Parameterized by VendorId/ProductId and (per the user's request) an
/// optional DevicePath to disambiguate multiple identical units — see devterm.runsettings.
/// Preflights via <see cref="RealDeviceReachability.IsHidDeviceAvailable"/> and reports
/// <see cref="Assert.Inconclusive(string)"/>, never a failure, when the device isn't currently
/// enumerated. Per the user's "pass as long as it doesn't fault or timeout" bar, this only asserts
/// that opening the connection and sending the command completed without throwing.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Hid)]
[TestCategory(TestCategories.Velleman_K8055)]
[TestCategory(TestCategories.Hardware)]
[TestClass]
public sealed class RealHardwareK8055Tests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    [TestMethod]
    [DataRow("K8055-1")]
    [DataRow("K8055-2")]
    [DataRow("K8055-3")]
    [DataRow("K8055-4")]
    public async Task RealDevice_ResetCounter1_CompletesWithoutFaultOrTimeout(string name)
    {
        var vendorIdText = GetProperty($"RealHid{name}-VendorId");
        var productIdText = GetProperty($"RealHid{name}-ProductId");
        var devicePath = GetProperty($"RealHid{name}-DevicePath");

        TestContext.WriteLine($"VendorId/ProductId/DevicePath: {vendorIdText}/{productIdText}/{devicePath}");

        if (string.IsNullOrEmpty(vendorIdText) || string.IsNullOrEmpty(productIdText)
            || !int.TryParse(vendorIdText, out var vendorId) || !int.TryParse(productIdText, out var productId))
        {
            Assert.Inconclusive($"No 'RealHid{name}-VendorId'/'RealHid{name}-ProductId' — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsHidDeviceAvailable(vendorId, productId, devicePath))
        {
            Assert.Inconclusive($"No K8055 HID device (VendorId {vendorId}, ProductId {productId}) is currently enumerated — is it plugged in?");
            return;
        }

        var options = Options.Create(new HidTransportOptions
        {
            VendorId = vendorId,
            ProductId = productId,
            DevicePath = string.IsNullOrEmpty(devicePath) ? null : devicePath,
        });

        await using var transport = new HidTransport(new SystemHidDeviceFactory(), options);
        await using var session = new Session(transport, new Pipeline([]));

        TestContext.WriteLine("Connecting...");
        await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Connected.");

        var controlSurface = new K8055ControlSurface(session);
        TestContext.WriteLine("Sending: resetCounter1");
        await controlSurface.InvokeAsync("resetCounter1", null, TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Received: (resetCounter1 is fire-and-forget; no reply frame expected)");

        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Closed.");
    }
}
