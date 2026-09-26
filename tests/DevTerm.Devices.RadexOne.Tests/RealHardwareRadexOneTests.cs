using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Hid;
using Microsoft.Extensions.Options;

namespace DevTerm.Devices.RadexOne.Tests;

/// <summary>
/// Opt-in test against a real Radex One HID device — same shape as
/// <c>DevTerm.Devices.Busylight.Tests.RealHardwareBusylightTests</c>. Sends a Read Data query and
/// waits for a reply to reach <see cref="RadexOneDecoder"/> via <see cref="Session.Output"/>.
///
/// <para>This test is also, by design, the first real check of this whole module's biggest unverified
/// assumption: the HID report framing in <see cref="RadexOneHidFraming"/> (leading report-ID byte,
/// 64-byte body). If it's wrong, this test will very likely time out waiting for a reply rather than
/// fail cleanly — see that type's doc comment before spending time debugging a timeout here as
/// something else.</para>
///
/// Preflights via <see cref="RealDeviceReachability.IsHidDeviceAvailable"/> and reports
/// <see cref="Assert.Inconclusive(string)"/>, never a failure, when the device isn't currently
/// enumerated — true as of this writing (2026-09-25): a live enumeration pass on the development
/// machine found no Radex One HID device attached, only an unrelated MSI "MYSTIC LIGHT" RGB
/// controller, despite it being expected to be attached. VendorId/ProductId are therefore still
/// unconfirmed and left blank in <c>devterm.runsettings</c> pending the device actually being
/// plugged in for a real session.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Hid)]
[TestCategory(TestCategories.Radex_One)]
[TestCategory(TestCategories.Hardware)]
[TestClass]
public sealed class RealHardwareRadexOneTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;

    [TestMethod]
    public async Task RealDevice_ReadData_ReceivesADecodedReply()
    {
        var vendorIdText = GetProperty("RealHidRadexOneVendorId");
        var productIdText = GetProperty("RealHidRadexOneProductId");
        var devicePath = GetProperty("RealHidRadexOneDevicePath");

        TestContext.WriteLine($"VendorId/ProductId/DevicePath: {vendorIdText}/{productIdText}/{devicePath}");

        if (string.IsNullOrEmpty(vendorIdText) || string.IsNullOrEmpty(productIdText)
            || !int.TryParse(vendorIdText, out var vendorId) || !int.TryParse(productIdText, out var productId))
        {
            Assert.Inconclusive("No 'RealHidRadexOneVendorId'/'RealHidRadexOneProductId' — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!RealDeviceReachability.IsHidDeviceAvailable(vendorId, productId, devicePath))
        {
            Assert.Inconclusive($"No Radex One HID device (VendorId {vendorId}, ProductId {productId}) is currently enumerated — is it plugged in?");
            return;
        }

        var options = Options.Create(new HidTransportOptions
        {
            VendorId = vendorId,
            ProductId = productId,
            DevicePath = string.IsNullOrEmpty(devicePath) ? null : devicePath,
        });

        await using var transport = new HidTransport(new SystemHidDeviceFactory(), options);
        await using var session = new Session(transport, new Pipeline([new RadexOneDecoder()]));

        string? received = null;
        session.Output += (_, output) => received = output.Text;

        TestContext.WriteLine("Connecting...");
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

        TestContext.WriteLine($"Received: {received ?? "(nothing — see this test class's doc comment about the HID framing assumption)"}");
        Assert.IsNotNull(received, "No reply received — either the device didn't respond, or RadexOneHidFraming's report-framing guess is wrong.");

        await session.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        TestContext.WriteLine("Closed.");
    }
}
