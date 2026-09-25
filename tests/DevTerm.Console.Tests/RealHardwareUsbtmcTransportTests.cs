using System.Buffers;
using System.Globalization;
using System.Text;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Usbtmc;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Opt-in tests of <see cref="UsbtmcTransport"/> itself against real Rigol USBTMC instruments -
/// below the <c>Session</c>/presenter layer that <see cref="RealHardwareUsbtmcTests"/>
/// exercises, reading raw reply bytes straight off <see cref="UsbtmcTransport.Input"/> so a binary
/// reply (a waveform) can be checked byte-for-byte. Each covers a behavior the protocol-conformance
/// rework changed (docs/design/features/usbtmc-protocol-conformance.md): unpaced back-to-back
/// queries (the DG1022's request-delay quirk), reopening the same transport, and exact framing of a
/// multi-packet binary reply (the DS1102E's padded <c>:WAV:DATA?</c>).
///
/// Uses the same <c>RealUsbtmc{Model}*</c> parameters from <c>devterm.runsettings</c> as the
/// Console tests; a device that isn't configured or isn't plugged in makes its tests Inconclusive.
/// [DoNotParallelize] (here and on RealHardwareUsbtmcTests, same assembly on purpose): two tests
/// claiming the same physical USB interface at once fail on the second claim, not on the device.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Usbtmc)]
[TestCategory(TestCategories.Scpi)]
[TestClass]
[DoNotParallelize]
public sealed class RealHardwareUsbtmcTransportTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Dg1022)]
    public Task Dg1022_UnpacedBackToBackQueries_AllAnswered() =>
        RunBackToBackQueriesAsync("RealUsbtmcDg1022", "\n", ["*IDN?", "OUTPut?", "FREQuency?", "VOLTage?"]);

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Dg1022)]
    public Task Dg1022_CloseAndReopenSameTransport_StillAnswers() =>
        RunReopenAsync("RealUsbtmcDg1022", "\n");

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Ds1102e)]
    public Task Ds1102e_UnpacedBackToBackQueries_AllAnswered() =>
        RunBackToBackQueriesAsync("RealUsbtmcDs1102e", string.Empty, ["*IDN?", ":MEAS:VPP? CHAN1", ":TIM:SCAL?", ":MEAS:FREQ? CHAN1"]);

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Ds1102e)]
    public Task Ds1102e_CloseAndReopenSameTransport_StillAnswers() =>
        RunReopenAsync("RealUsbtmcDs1102e", string.Empty);

    /// <summary>
    /// Confirmed on the bench (docs/test/2026-09-25-18-03-06.md): the DS1102E answers
    /// <c>:WAV:DATA? CHAN1</c> with a header declaring exactly the IEEE 488.2 block
    /// (<c>#8</c> + 8 length digits + the samples), followed by 10 extra non-sample padding bytes in
    /// the same transfer. The reply surfaced must be exactly the block - no padding, nothing
    /// truncated - and the padding must not leak into the next query's reply.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Ds1102e)]
    public async Task Ds1102e_WaveformRead_ReturnsExactlyTheIeeeBlockThenNextQueryIsClean()
    {
        await using var transport = await OpenOrInconclusiveAsync("RealUsbtmcDs1102e");
        if (transport is null)
        {
            return;
        }

        var waveform = await QueryAsync(transport, ":WAV:DATA? CHAN1");
        TestContext.WriteLine($"Waveform reply: {waveform.Length} byte(s), starts '{Encoding.ASCII.GetString(waveform, 0, Math.Min(10, waveform.Length))}'");

        Assert.IsGreaterThanOrEqualTo(2, waveform.Length);
        Assert.AreEqual((byte)'#', waveform[0]);
        var digitCount = waveform[1] - '0';
        Assert.IsTrue(digitCount is >= 1 and <= 9, $"IEEE block digit count '{(char)waveform[1]}' is not 1-9.");

        var declaredLength = int.Parse(Encoding.ASCII.GetString(waveform, 2, digitCount), CultureInfo.InvariantCulture);
        Assert.AreEqual(2 + digitCount + declaredLength, waveform.Length, "Reply should be exactly the IEEE block: no trailing padding, nothing truncated.");

        var idn = Encoding.ASCII.GetString(await QueryAsync(transport, "*IDN?"));
        TestContext.WriteLine($"Next *IDN?: {idn}");
        StringAssert.StartsWith(idn, "Rigol", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Dg1062z)]
    public Task Dg1062z_UnpacedBackToBackQueries_AllAnswered() =>
        RunBackToBackQueriesAsync("RealUsbtmcDg1062z", "\n", ["*IDN?", "SOURce1:FREQuency?", "SOURce1:APPLy?", "OUTPut1?"]);

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Dg1062z)]
    public Task Dg1062z_CloseAndReopenSameTransport_StillAnswers() =>
        RunReopenAsync("RealUsbtmcDg1062z", "\n");

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Dm3058e)]
    public Task Dm3058e_UnpacedBackToBackQueries_AllAnswered() =>
        RunBackToBackQueriesAsync("RealUsbtmcDm3058e", "\n", ["*IDN?", "SYSTem:VERSion?", "*IDN?", "SYSTem:VERSion?"]);

    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Dm3058e)]
    public Task Dm3058e_CloseAndReopenSameTransport_StillAnswers() =>
        RunReopenAsync("RealUsbtmcDm3058e", "\n");

    // Two passes over the queries with no pause between them - a device that drops a read request
    // sent too soon after its query (the DG1022, see UsbtmcDeviceQuirks) fails this without the
    // quirk's delay, and any reply left unread would answer the wrong query.
    private async Task RunBackToBackQueriesAsync(string parameterPrefix, string terminator, string[] queries)
    {
        await using var transport = await OpenOrInconclusiveAsync(parameterPrefix);
        if (transport is null)
        {
            return;
        }

        var expectedIdn = GetProperty($"{parameterPrefix}ExpectedIdnReply");
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var query in queries)
            {
                var reply = Encoding.ASCII.GetString(await QueryAsync(transport, query + terminator));
                TestContext.WriteLine($"{query} -> {reply.TrimEnd()}");

                Assert.IsFalse(string.IsNullOrWhiteSpace(reply), $"Expected a reply to '{query}'.");
                if (query == "*IDN?" && !string.IsNullOrEmpty(expectedIdn))
                {
                    Assert.Contains(expectedIdn, reply);
                }
            }
        }
    }

    // CloseAsync then OpenAsync on the same instance, twice - Session's Connect/Disconnect toggle
    // does exactly this, and each reopen must start from a clean device state.
    private async Task RunReopenAsync(string parameterPrefix, string terminator)
    {
        await using var transport = await OpenOrInconclusiveAsync(parameterPrefix);
        if (transport is null)
        {
            return;
        }

        for (var cycle = 0; cycle < 3; cycle++)
        {
            if (cycle > 0)
            {
                await transport.CloseAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
                await transport.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
            }

            var idn = Encoding.ASCII.GetString(await QueryAsync(transport, "*IDN?" + terminator));
            TestContext.WriteLine($"Cycle {cycle}: {idn.TrimEnd()}");
            StringAssert.StartsWith(idn, "Rigol", StringComparison.OrdinalIgnoreCase);
        }
    }

    // WriteAsync returns only after a query's whole reply has been read and flushed into Input,
    // so a non-blocking TryRead picks up exactly that reply.
    private async Task<byte[]> QueryAsync(UsbtmcTransport transport, string command)
    {
        await transport.WriteAsync(Encoding.ASCII.GetBytes(command), TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        if (!transport.Input.TryRead(out var result))
        {
            return [];
        }

        var bytes = result.Buffer.ToArray();
        transport.Input.AdvanceTo(result.Buffer.End);
        return bytes;
    }

    private async Task<UsbtmcTransport?> OpenOrInconclusiveAsync(string parameterPrefix)
    {
        var vendorIdText = GetProperty($"{parameterPrefix}VendorId");
        var productIdText = GetProperty($"{parameterPrefix}ProductId");
        var serialNumber = GetProperty($"{parameterPrefix}SerialNumber");

        if (!int.TryParse(vendorIdText, out var vendorId) || !int.TryParse(productIdText, out var productId))
        {
            Assert.Inconclusive($"No '{parameterPrefix}VendorId'/'{parameterPrefix}ProductId' - run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return null;
        }

        if (!RealDeviceReachability.IsUsbtmcDeviceAvailable(vendorId, productId, serialNumber))
        {
            Assert.Inconclusive($"No USBTMC device (VendorId {vendorId}, ProductId {productId}, SerialNumber '{serialNumber}') is currently enumerated - is it plugged in and powered on?");
            return null;
        }

        var transport = new UsbtmcTransport(
            new SystemUsbtmcDeviceFactory(),
            Options.Create(new UsbtmcTransportOptions
            {
                VendorId = vendorId,
                ProductId = productId,
                SerialNumber = string.IsNullOrEmpty(serialNumber) ? null : serialNumber,
            }));

        await transport.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        return transport;
    }

    private string? GetProperty(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;
}
