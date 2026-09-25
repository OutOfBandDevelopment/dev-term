using System.Diagnostics;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>
/// Opt-in tests against real hardware over the network — two Tektronix 2230 oscilloscopes (pre-SCPI,
/// answer <c>ID?</c>) and a Tektronix TDS2024 (SCPI, answers <c>*IDN?</c> instead), all reachable via
/// a serial-to-Ethernet bridge; the 2230s are the same devices referenced by
/// <c>src/DevTerm.Console/Properties/launchSettings.json</c>'s "TCP 192.168.0.107/.108:23 ASCII"
/// profiles. Parameterized via a <c>.runsettings</c> file (see <c>devterm.runsettings</c> at the
/// repo root) rather than hardcoded, so this suite degrades to <see cref="Assert.Inconclusive(string)"/>
/// (not a failure) when run without one — <c>dotnet test</c> alone stays hardware-free;
/// <c>dotnet test --settings devterm.runsettings</c> exercises the real devices when online. The
/// query text and expected reply both differ per device family, and both are themselves
/// `.runsettings` parameters (their *names* are the `DataRow` parameters, not literal strings baked
/// into the test) — a value nobody has actually verified against real hardware yet (the TDS2024's
/// expected reply, see below) can't silently assert something guessed, and adding a fourth real
/// device later needs only new runsettings parameters plus one new `DataRow`, no code change.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestClass]
public sealed class RealHardwareCliTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly string _consoleAppDirectory = AppContext.BaseDirectory.Replace(
        Path.Combine("tests", "DevTerm.Console.Tests"),
        Path.Combine("src", "DevTerm.Console"));

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private string? GetProperty(string name) => TestContext.Properties.ContainsKey(name) ? TestContext.Properties[name] as string : null;

    [TestMethod]
    [DataRow("RealTcpDeviceHost1", "RealTcpDeviceHost1Query", "RealTcpDeviceHost1ExpectedReply")]
    [DataRow("RealTcpDeviceHost2", "RealTcpDeviceHost2Query", "RealTcpDeviceHost2ExpectedReply")]
    // TDS2024 is SCPI, not pre-SCPI like the 2230s — *IDN? instead of ID? (see
    // RealTcpDeviceHost3Query in devterm.runsettings). Its expected-reply value there ("TEKTRONIX",
    // the standard *IDN? response's manufacturer field) hasn't been verified against this specific
    // real unit yet — see CLAUDE.md's "verify against real hardware" note. Any of these three
    // parameters missing/blank in a runsettings file reports Inconclusive below, same as a missing
    // host, rather than asserting an unverified guess.
    [DataRow("RealTcpDeviceHost3", "RealTcpDeviceHost3Query", "RealTcpDeviceHost3ExpectedReply")]
    public async Task CliMode_AgainstRealDevice_AnswersIdentityQuery(string hostParameterName, string queryParameterName, string expectedReplyParameterName)
    {
        var host = GetProperty(hostParameterName);
        var port = GetProperty("RealTcpDevicePort");
        var query = GetProperty(queryParameterName);
        var expectedReplySubstring = GetProperty(expectedReplyParameterName);

        TestContext.WriteLine($"Host: {host}");
        TestContext.WriteLine($"Port: {port}");
        TestContext.WriteLine($"Query: {query}");
        TestContext.WriteLine($"Expected Reply Subtring: {expectedReplySubstring}");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port) || string.IsNullOrEmpty(query) || string.IsNullOrEmpty(expectedReplySubstring))
        {
            Assert.Inconclusive($"No '{hostParameterName}'/'RealTcpDevicePort'/'{queryParameterName}'/'{expectedReplyParameterName}' — run with 'dotnet test --settings devterm.runsettings' to exercise this against real hardware.");
            return;
        }

        if (!int.TryParse(port, out var portNumber))
        {
            Assert.Inconclusive($"'RealTcpDevicePort' value '{port}' is not a valid port number.");
            return;
        }

        if (!await RealDeviceReachability.IsTcpReachableAsync(host, portNumber, TestContext.CancellationToken))
        {
            Assert.Inconclusive($"Real device at {host}:{port} is not reachable (TCP connect attempt timed out/refused within {RealDeviceReachability.DefaultTimeout.TotalSeconds}s) — is it powered on and networked?");
            return;
        }

        var startInfo = new ProcessStartInfo(
            "dotnet",
            $"\"{Path.Combine(_consoleAppDirectory, "DevTerm.Console.dll")}\" --transport tcp --host {host} --port {port} --presenter ascii --lineending Cr --cli true")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)!;

        await process.StandardInput.WriteLineAsync(query);
        await process.StandardInput.FlushAsync(TestContext.CancellationToken);

        string? line;
        do
        {
            line = await process.StandardOutput.ReadLineAsync(TestContext.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.CancellationToken);
        }
        while (line is not null && !line.Contains("[ascii]", StringComparison.Ordinal));

        TestContext.WriteLine($"Reply: {line}");

        Assert.IsNotNull(line, $"Expected a decoded reply from the real device at {host}:{port} before the process ran out of output.");
        Assert.Contains(expectedReplySubstring, line);

        process.StandardInput.Close();
        await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(_timeout, TestContext.CancellationToken);
        Assert.AreEqual(0, process.ExitCode);
    }
}
