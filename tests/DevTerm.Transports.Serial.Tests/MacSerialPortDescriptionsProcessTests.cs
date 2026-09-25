using System.Diagnostics;
using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Serial.Tests;

/// <summary>
/// The bounded process runner <see cref="MacSerialPortDescriptions"/> uses for <c>ioreg</c>, driven
/// with this OS's own stand-ins (there's no <c>ioreg</c> off macOS) — Integration because it starts
/// real processes.
/// </summary>
[TestCategory(TestCategories.Integration)]
[TestCategory(TestCategories.Serial)]
[TestClass]
public sealed class MacSerialPortDescriptionsProcessTests
{
    [TestMethod]
    public void RunProcess_WhenTheExecutableDoesNotExist_ReturnsNull() =>
        Assert.IsNull(MacSerialPortDescriptions.RunProcess(
            Path.Combine(Path.GetTempPath(), "devterm-no-such-ioreg-" + Guid.NewGuid().ToString("N")),
            [],
            TimeSpan.FromSeconds(5)));

    [TestMethod]
    public void RunProcess_ReturnsStandardOutput()
    {
        var output = OperatingSystem.IsWindows()
            ? MacSerialPortDescriptions.RunProcess("cmd.exe", ["/c", "echo hello"], TimeSpan.FromSeconds(10))
            : MacSerialPortDescriptions.RunProcess("/bin/sh", ["-c", "echo hello"], TimeSpan.FromSeconds(10));

        Assert.AreEqual("hello", output?.Trim());
    }

    [TestMethod]
    public void RunProcess_WhenTheProcessExitsNonZero_ReturnsNull()
    {
        var output = OperatingSystem.IsWindows()
            ? MacSerialPortDescriptions.RunProcess("cmd.exe", ["/c", "echo partial & exit 3"], TimeSpan.FromSeconds(10))
            : MacSerialPortDescriptions.RunProcess("/bin/sh", ["-c", "echo partial; exit 3"], TimeSpan.FromSeconds(10));

        Assert.IsNull(output);
    }

    [TestMethod]
    public void RunProcess_WhenTheProcessOutlivesTheTimeout_KillsItAndReturnsNull()
    {
        var stopwatch = Stopwatch.StartNew();
        var output = OperatingSystem.IsWindows()
            ? MacSerialPortDescriptions.RunProcess("ping.exe", ["-n", "60", "127.0.0.1"], TimeSpan.FromMilliseconds(500))
            : MacSerialPortDescriptions.RunProcess("/bin/sleep", ["60"], TimeSpan.FromMilliseconds(500));

        Assert.IsNull(output);
        Assert.IsLessThan(TimeSpan.FromSeconds(15), stopwatch.Elapsed, "RunProcess waited far past its timeout.");
    }
}
