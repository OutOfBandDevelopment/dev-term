using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DevTerm.Console.Tests;

/// <summary>
/// Process-level automation of the real, built console app — spawns the actual
/// <c>DevTerm.Console.dll</c> (via the sibling <c>src/DevTerm.Console/bin/&lt;config&gt;/&lt;tfm&gt;</c>
/// output this test's own build lays down next to), pipes real stdin/stdout, and for the
/// end-to-end case drives it against a real local TCP socket this test controls — no real
/// hardware, but a real transport and a real process, not a call into internal methods.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class ConsoleAppCliTests
{
    private static readonly string _consoleAppDirectory = AppContext.BaseDirectory.Replace(
        Path.Combine("tests", "DevTerm.Console.Tests"),
        Path.Combine("src", "DevTerm.Console"));

    private static ProcessStartInfo BuildStartInfo(string arguments) => new("dotnet", $"\"{Path.Combine(_consoleAppDirectory, "DevTerm.Console.dll")}\" {arguments}")
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task ListPorts_ExitsZeroWithoutCrashing()
    {
        using var process = Process.Start(BuildStartInfo("--listports true"))!;
        await process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        Assert.AreEqual(0, process.ExitCode);
    }

    [TestMethod]
    public async Task ListHidDevices_ExitsZeroWithoutCrashing()
    {
        using var process = Process.Start(BuildStartInfo("--listhiddevices true"))!;
        await process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        Assert.AreEqual(0, process.ExitCode);
    }

    [TestMethod]
    public async Task ListHidDevices_WithVendorAndProductIdFilter_ExitsZeroAndOmitsNonMatchingDevices()
    {
        // 65535/65535 is not a real, assigned USB vendor/product id, so whatever's actually plugged
        // into the machine running this test is guaranteed not to match — proves the filter narrows
        // the list (to empty here) rather than just proving the flags parse without crashing.
        using var process = Process.Start(BuildStartInfo("--listhiddevices true --vendorid 65535 --productid 65535"))!;
        var stdout = await process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        Assert.AreEqual(0, process.ExitCode);
        Assert.AreEqual(string.Empty, stdout.Trim());
    }

    [TestMethod]
    public async Task ListUsbtmcDevices_ExitsZeroWithoutCrashing()
    {
        using var process = Process.Start(BuildStartInfo("--listusbtmcdevices true"))!;
        await process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        Assert.AreEqual(0, process.ExitCode);
    }

    [TestMethod]
    public async Task ListUsbtmcDevices_WithVendorAndProductIdFilter_ExitsZeroAndOmitsNonMatchingDevices()
    {
        using var process = Process.Start(BuildStartInfo("--listusbtmcdevices true --vendorid 65535 --productid 65535"))!;
        var stdout = await process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        Assert.AreEqual(0, process.ExitCode);
        Assert.AreEqual(string.Empty, stdout.Trim());
    }

    [TestMethod]
    public async Task UnknownTransport_PrintsErrorAndUsage_ExitsOne()
    {
        using var process = Process.Start(BuildStartInfo("--transport carrier-pigeon --cli true"))!;
        var stderr = await process.StandardError.ReadToEndAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);

        Assert.AreEqual(1, process.ExitCode);
        Assert.Contains("Unknown transport", stderr);
        Assert.Contains("Usage:", stderr);
    }

    [TestMethod]
    public async Task CliMode_OverTcp_SendsTypedLineAndPrintsDecodedReply()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);

        using var process = Process.Start(BuildStartInfo(
            $"--transport tcp --host 127.0.0.1 --port {port} --presenter ascii --lineending Cr --cli true"))!;

        using var client = await acceptTask.AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
        using var stream = client.GetStream();

        await process.StandardInput.WriteLineAsync("ID?");
        await process.StandardInput.FlushAsync(TestContext.CancellationToken);

        var requestBuffer = new byte[64];
        var requestLength = await stream.ReadAsync(requestBuffer, TestContext.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
        Assert.AreEqual("ID?\r", Encoding.ASCII.GetString(requestBuffer, 0, requestLength),
            "The CLI should append the configured Cr line ending to what was typed.");

        await stream.WriteAsync(Encoding.ASCII.GetBytes("ID TESTDEVICE\r"), TestContext.CancellationToken);

        string? line;
        do
        {
            line = await process.StandardOutput.ReadLineAsync(TestContext.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
        }
        while (line is not null && !line.Contains("ID TESTDEVICE", StringComparison.Ordinal));

        Assert.IsNotNull(line, "Expected the decoded reply to appear on stdout before the process ran out of output.");
        Assert.Contains("[ascii]", line);

        process.StandardInput.Close();
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        Assert.AreEqual(0, process.ExitCode);
    }

    [TestMethod]
    public async Task CliMode_WithSeveralPresentersAndAHexParser_SendsHexBytesAndPrintsEveryPresentersView()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync(TestContext.CancellationToken);

        // Presenter is a comma-separated list on the command line (a JSON array in a profile), and
        // Parser - what encodes typed lines - is independent of which presenters display.
        using var process = Process.Start(BuildStartInfo(
            $"--transport tcp --host 127.0.0.1 --port {port} --presenter ascii,hex --parser hex --lineending None --cli true"))!;

        using var client = await acceptTask.AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
        using var stream = client.GetStream();

        await process.StandardInput.WriteLineAsync("49 44");
        await process.StandardInput.FlushAsync(TestContext.CancellationToken);

        var requestBuffer = new byte[64];
        var requestLength = await stream.ReadAsync(requestBuffer, TestContext.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
        Assert.AreEqual("ID", Encoding.ASCII.GetString(requestBuffer, 0, requestLength),
            "'49 44' typed under the hex parser should go out as the two bytes 0x49 0x44.");

        await stream.WriteAsync(Encoding.ASCII.GetBytes("OK\r"), TestContext.CancellationToken);

        var lines = new List<string>();
        string? line;
        while (!(lines.Any(l => l.StartsWith("[ascii]", StringComparison.Ordinal)) && lines.Any(l => l.StartsWith("[hex]", StringComparison.Ordinal)))
               && (line = await process.StandardOutput.ReadLineAsync(TestContext.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.CancellationToken)) is not null)
        {
            lines.Add(line);
        }

        var seen = string.Join(" | ", lines);
        Assert.Contains(l => l.StartsWith("[ascii]", StringComparison.Ordinal) && l.Contains("OK", StringComparison.Ordinal), lines, seen);
        Assert.Contains(l => l.StartsWith("[hex]", StringComparison.Ordinal) && l.Contains("4F4B0D", StringComparison.Ordinal), lines, seen);

        process.StandardInput.Close();
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        Assert.AreEqual(0, process.ExitCode);
    }

    public TestContext TestContext { get; set; }
}
