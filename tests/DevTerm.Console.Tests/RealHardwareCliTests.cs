using System.Diagnostics;

namespace DevTerm.Console.Tests;

/// <summary>
/// Opt-in tests against real hardware over the network — a Tektronix 2230 oscilloscope reachable
/// via a serial-to-Ethernet bridge, the same device referenced by
/// <c>src/DevTerm.Console/Properties/launchSettings.json</c>'s "TCP 192.168.0.107/.108:23 ASCII"
/// profiles. Parameterized via a <c>.runsettings</c> file (see <c>devterm.runsettings</c> at the
/// repo root) rather than hardcoded, so this suite degrades to <see cref="Assert.Inconclusive(string)"/>
/// (not a failure) when run without one — <c>dotnet test</c> alone stays hardware-free;
/// <c>dotnet test --settings devterm.runsettings</c> exercises the real device when it's online.
/// </summary>
[TestCategory("DEV-LOCAL")]
[TestClass]
public sealed class RealHardwareCliTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly string ConsoleAppDirectory = AppContext.BaseDirectory.Replace(
        Path.Combine("tests", "DevTerm.Console.Tests"),
        Path.Combine("src", "DevTerm.Console"));

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow("RealTcpDeviceHost1")]
    [DataRow("RealTcpDeviceHost2")]
    public async Task CliMode_AgainstRealDevice_AnswersIdQuery(string hostParameterName)
    {
        var host = TestContext.Properties.ContainsKey(hostParameterName) ? TestContext.Properties[hostParameterName] as string : null;
        var port = TestContext.Properties.ContainsKey("RealTcpDevicePort") ? TestContext.Properties["RealTcpDevicePort"] as string : null;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port))
        {
            Assert.Inconclusive($"No '{hostParameterName}'/'RealTcpDevicePort' — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        var startInfo = new ProcessStartInfo(
            "dotnet",
            $"\"{Path.Combine(ConsoleAppDirectory, "DevTerm.Console.dll")}\" --transport tcp --host {host} --tcpport {port} --presenter ascii --lineending Cr --cli true")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)!;

        await process.StandardInput.WriteLineAsync("ID?");
        await process.StandardInput.FlushAsync();

        string? line;
        do
        {
            line = await process.StandardOutput.ReadLineAsync().WaitAsync(Timeout);
        }
        while (line is not null && !line.Contains("[ascii]", StringComparison.Ordinal));

        Assert.IsNotNull(line, $"Expected a decoded reply from the real device at {host}:{port} before the process ran out of output.");
        StringAssert.Contains(line, "TEK/2230");

        process.StandardInput.Close();
        await process.WaitForExitAsync().WaitAsync(Timeout);
        Assert.AreEqual(0, process.ExitCode);
    }
}
