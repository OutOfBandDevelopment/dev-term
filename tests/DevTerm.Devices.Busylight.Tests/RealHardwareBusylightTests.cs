using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Hid;
using Microsoft.Extensions.Options;

namespace DevTerm.Devices.Busylight.Tests;

/// <summary>
/// Opt-in test against a real Kuando Busylight HID device — same reasoning and shape as
/// <c>DevTerm.Devices.K8055.Tests.RealHardwareK8055Tests</c> (binary fixed-length HID frames, no CLI
/// text loop to drive, so a real <see cref="HidTransport"/> plus <see cref="BusylightControlSurface"/>
/// are constructed directly). Sets color to "Off" then applies it — turns the light off, which is
/// harmless real-hardware behavior regardless of the light's current state. Preflights via
/// <see cref="RealDeviceReachability.IsHidDeviceAvailable"/> and reports
/// <see cref="Assert.Inconclusive(string)"/>, never a failure, when the device isn't currently
/// enumerated.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Hid)]
[TestCategory(TestCategories.Kuando_Busylight)]
[TestCategory(TestCategories.Hardware)]
[TestClass]
public sealed class RealHardwareBusylightTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    [TestMethod]
    public async Task RealDevice_SetColorOffAndApply_CompletesWithoutFaultOrTimeout()
    {
        var vendorIdText = GetProperty("RealHidBusylightVendorId");
        var productIdText = GetProperty("RealHidBusylightProductId");
        var devicePath = GetProperty("RealHidBusylightDevicePath");

        TestContext.WriteLine($"VendorId/ProductId/DevicePath: {vendorIdText}/{productIdText}/{devicePath}");

        if (string.IsNullOrEmpty(vendorIdText) || string.IsNullOrEmpty(productIdText)
            || !int.TryParse(vendorIdText, out var vendorId) || !int.TryParse(productIdText, out var productId))
        {
            Assert.Inconclusive("No 'RealHidBusylightVendorId'/'RealHidBusylightProductId' — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsHidDeviceAvailable(vendorId, productId, devicePath))
        {
            Assert.Inconclusive($"No Busylight HID device (VendorId {vendorId}, ProductId {productId}) is currently enumerated — is it plugged in?");
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

        var controlSurface = new BusylightControlSurface(session);
        TestContext.WriteLine("Sending: color=Off");
        await controlSurface.InvokeAsync("color", "Off", TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Sending: apply");
        await controlSurface.InvokeAsync("apply", null, TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Received: (color/apply are fire-and-forget HID writes; no reply frame expected)");

        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Closed.");
    }
}
