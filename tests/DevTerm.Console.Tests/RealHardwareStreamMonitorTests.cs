using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Tcp;
using DevTerm.Transports.Usbtmc;
using Microsoft.Extensions.Options;

namespace DevTerm.Console.Tests;

/// <summary>
/// Opt-in: the Stream Monitor against a real instrument's screen dump, through a real
/// <see cref="Session"/> over USBTMC. Uses the same <c>RealUsbtmcDg1062z*</c> parameters as the other
/// USBTMC hardware tests; a device that isn't configured or plugged in makes it Inconclusive.
/// [DoNotParallelize]: two tests claiming one USB interface at once fail on the second claim.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Usbtmc)]
[TestClass]
[DoNotParallelize]
public sealed class RealHardwareStreamMonitorTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// <c>HCOPy:SDUMp:DATA?</c> answers <c>#9</c> + a 9-digit length + a 24-bit 320x240 BMP. The monitor must
    /// strip the IEEE 488.2 header and save the BMP itself, as one capture.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Rigol_Dg1062z)]
    public async Task Dg1062z_ScreenDump_IsCapturedAsOneBmp()
    {
        var vendor = Parameter("RealUsbtmcDg1062zVendorId");
        var product = Parameter("RealUsbtmcDg1062zProductId");
        var serial = Parameter("RealUsbtmcDg1062zSerialNumber");
        if (!int.TryParse(vendor, out var vendorId) || !int.TryParse(product, out var productId))
        {
            Assert.Inconclusive("No RealUsbtmcDg1062z parameters - run with 'dotnet test --settings devterm.runsettings'.");
            return;
        }

        if (!RealDeviceReachability.IsUsbtmcDeviceAvailable(vendorId, productId, serial))
        {
            Assert.Inconclusive("The DG1062Z isn't enumerated - is it plugged in and powered on?");
            return;
        }

        var exportDirectory = Path.Combine(Path.GetTempPath(), "devterm-hw-streammonitor-" + Guid.NewGuid().ToString("N"));
        var transport = new UsbtmcTransport(
            new SystemUsbtmcDeviceFactory(),
            Options.Create(new UsbtmcTransportOptions { VendorId = vendorId, ProductId = productId, SerialNumber = string.IsNullOrEmpty(serial) ? null : serial }));
        await using var session = new Session(transport, new Pipeline([new RawPresenter()]));
        using var monitor = new StreamMonitor();
        monitor.SetSession(session, "DG1062Z", exportDirectory);
        monitor.Start();
        var captured = new TaskCompletionSource<StreamMonitorCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.CaptureAdded += (_, c) => captured.TrySetResult(c);

        try
        {
            await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
            await session.SendAsync(Encoding.ASCII.GetBytes("HCOPy:SDUMp:DATA?\n"), TestContext.CancellationToken);
            var capture = await captured.Task.WaitAsync(_timeout, TestContext.CancellationToken);

            TestContext.WriteLine($"Kind={capture.Capture.Kind.DisplayName} Bytes={capture.Capture.Data.Length} End={capture.Capture.EndReason} Declared={capture.Capture.WasDeclared} Saved={capture.SavedPath} Error={capture.SaveError}");
            Assert.IsNull(capture.SaveError);
            Assert.AreEqual("bmp", capture.Capture.Kind.Extension);
            Assert.AreEqual((byte)'B', capture.Capture.Data[0]);
            Assert.AreEqual((byte)'M', capture.Capture.Data[1]);
            Assert.IsTrue(File.Exists(capture.SavedPath), "Expected the BMP to be saved.");
            Assert.HasCount(1, monitor.Captures);
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// The TDS2024's <c>HARDCOPY START</c> (format BMP, port RS232) streams a raw BMP down the serial bridge
    /// at 19200 baud, so it takes a while and has no SCPI block header.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Tektronix_Tds2024)]
    public async Task Tds2024_HardCopy_IsCapturedAsOneBmp()
    {
        var host = Parameter("RealTcpDeviceHost3");
        var portText = Parameter("RealTcpDevicePort3") ?? "23";
        if (string.IsNullOrEmpty(host) || !int.TryParse(portText, out var port))
        {
            Assert.Inconclusive("No RealTcpDeviceHost3 parameter - run with 'dotnet test --settings devterm.runsettings'.");
            return;
        }

        if (!await RealDeviceReachability.IsTcpReachableAsync(host, port, TestContext.CancellationToken))
        {
            Assert.Inconclusive("The TDS2024 bridge isn't reachable.");
            return;
        }

        var exportDirectory = Path.Combine(Path.GetTempPath(), "devterm-hw-streammonitor-" + Guid.NewGuid().ToString("N"));
        var transport = new TcpTransport(
            new SystemTcpConnectionSource(),
            Options.Create(new TcpTransportOptions { Host = host, Port = port, WriteByteDelayMs = 50 }));
        await using var session = new Session(transport, new Pipeline([new RawPresenter()]));
        using var monitor = new StreamMonitor();
        monitor.SetSession(session, "TDS2024", exportDirectory);
        monitor.Start();
        var captured = new TaskCompletionSource<StreamMonitorCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.CaptureAdded += (_, c) => captured.TrySetResult(c);

        try
        {
            await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
            // A failed run of the other-formats test can leave the scope on another format, so set BMP explicitly.
            await session.SendAsync(Encoding.ASCII.GetBytes("HARDCopy:FORMat BMP\n"), TestContext.CancellationToken);
            await Task.Delay(500, TestContext.CancellationToken);
            await session.SendAsync(Encoding.ASCII.GetBytes("HARDCopy START\n"), TestContext.CancellationToken);
            var capture = await captured.Task.WaitAsync(TimeSpan.FromMinutes(3), TestContext.CancellationToken);

            TestContext.WriteLine($"Kind={capture.Capture.Kind.DisplayName} Bytes={capture.Capture.Data.Length} End={capture.Capture.EndReason} Declared={capture.Capture.WasDeclared} Saved={capture.SavedPath} Error={capture.SaveError}");
            Assert.IsNull(capture.SaveError);
            Assert.AreEqual("bmp", capture.Capture.Kind.Extension);
            Assert.IsTrue(File.Exists(capture.SavedPath), "Expected the BMP to be saved.");
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// The TDS2024's <c>HARDCopy:FORMat</c> picks what <c>HARDCopy START</c> streams, so one scope yields several
    /// real captures: EPSIMAGE is PostScript, LASERJET is PCL, PCX/TIFF/RLE are rasters. The format is restored to
    /// BMP afterwards. Logs what the sniffer called each capture and keeps the bytes under the test results folder.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategories.Hardware)]
    [TestCategory(TestCategories.Tektronix_Tds2024)]
    [Timeout(360000)]
    [DataRow("EPSIMAGE")]
    [DataRow("TIFF")]
    [DataRow("PCX")]
    [DataRow("LASERJET")]
    public async Task Tds2024_HardCopy_OtherFormats_AreCaptured(string format)
    {
        var host = Parameter("RealTcpDeviceHost3");
        var portText = Parameter("RealTcpDevicePort3") ?? "23";
        if (string.IsNullOrEmpty(host) || !int.TryParse(portText, out var port))
        {
            Assert.Inconclusive("No RealTcpDeviceHost3 parameter - run with 'dotnet test --settings devterm.runsettings'.");
            return;
        }

        if (!await RealDeviceReachability.IsTcpReachableAsync(host, port, TestContext.CancellationToken))
        {
            Assert.Inconclusive("The TDS2024 bridge isn't reachable.");
            return;
        }

        var exportDirectory = Path.Combine(Path.GetTempPath(), "devterm-hw-streammonitor-" + format + "-" + Guid.NewGuid().ToString("N"));
        var transport = new TcpTransport(
            new SystemTcpConnectionSource(),
            Options.Create(new TcpTransportOptions { Host = host, Port = port, WriteByteDelayMs = 50 }));
        await using var session = new Session(transport, new Pipeline([new RawPresenter()]));
        using var monitor = new StreamMonitor();
        monitor.SetSession(session, "TDS2024", exportDirectory);
        monitor.Start();
        var captured = new TaskCompletionSource<StreamMonitorCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.CaptureAdded += (_, c) => captured.TrySetResult(c);

        try
        {
            await session.OpenAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
            await session.SendAsync(Encoding.ASCII.GetBytes("HARDCopy:FORMat " + format + "\n"), TestContext.CancellationToken);
            await Task.Delay(500, TestContext.CancellationToken);
            await session.SendAsync(Encoding.ASCII.GetBytes("HARDCopy START\n"), TestContext.CancellationToken);
            var capture = await captured.Task.WaitAsync(TimeSpan.FromMinutes(5), TestContext.CancellationToken);

            var head = Convert.ToHexString(capture.Capture.Data.AsSpan(0, Math.Min(24, capture.Capture.Data.Length)));
            TestContext.WriteLine($"Format={format} Kind={capture.Capture.Kind.DisplayName} Bytes={capture.Capture.Data.Length} End={capture.Capture.EndReason} Declared={capture.Capture.WasDeclared} Head={head} Saved={capture.SavedPath} Error={capture.SaveError}");
            var keep = Path.Combine(Path.GetTempPath(), "devterm-tds2024-" + format.ToLowerInvariant() + ".bin");
            File.WriteAllBytes(keep, capture.Capture.Data);
            TestContext.WriteLine("Raw bytes kept at " + keep);
            Assert.IsNull(capture.SaveError);
        }
        finally
        {
            try
            {
                await session.SendAsync(Encoding.ASCII.GetBytes("HARDCopy:FORMat BMP\n"), CancellationToken.None);
            }
            catch (Exception)
            {
                // Best effort: report below if the scope was left in this format.
            }

            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    private string? Parameter(string name) => TestContext.Properties.TryGetValue(name, out var value) ? value as string : null;
}
