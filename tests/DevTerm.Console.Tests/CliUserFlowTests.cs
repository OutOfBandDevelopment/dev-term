using System.Diagnostics;
using System.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>The shared user flows, driven through the real built console app's stdin/stdout in <c>--cli true</c> mode.</summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
public sealed class CliUserFlowTests : UserFlowTestsBase
{
    private static readonly string _consoleAppDirectory = AppContext.BaseDirectory.Replace(
        Path.Combine("tests", "DevTerm.Console.Tests"),
        Path.Combine("src", "DevTerm.Console"));

    protected override async Task RunAsync(Func<IFrontEndDriver, Task> flow)
    {
        using var job = new ChildProcessJob();
        var info = new ProcessStartInfo("dotnet", $"\"{Path.Combine(_consoleAppDirectory, "DevTerm.Console.dll")}\" --transport loopback --presenter ascii --cli true")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info)!;
        job.Add(process);
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(e.Data);
                }
            }
        };
        process.BeginOutputReadLine();
        _ = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        var driver = new Driver(process, output);
        Assert.IsTrue(await driver.WaitForOutputAsync("Connected to Loopback", TimeSpan.FromSeconds(15)), "The console app never reported a connection.");
        try
        {
            await flow(driver);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private sealed class Driver(Process process, StringBuilder output) : IFrontEndDriver
    {
        public string Name => "CLI";

        public bool CanDisconnect => false;

        public async Task SendAsync(string line) => await process.StandardInput.WriteLineAsync(line);

        public Task<string> OutputAsync()
        {
            lock (output)
            {
                return Task.FromResult(output.ToString());
            }
        }

        public async Task<bool> WaitForOutputAsync(string text, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if ((await OutputAsync()).Contains(text, StringComparison.Ordinal))
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return false;
        }

        public Task<bool> IsConnectedAsync() => Task.FromResult(!process.HasExited);

        public Task DisconnectAsync() => throw new NotSupportedException();

        public Task ReconnectAsync() => throw new NotSupportedException();

        public bool CanSwitchProfile => false;

        public Task SwitchToLoopbackProfileAsync() => throw new NotSupportedException();

        public bool CanLog => false;

        public Task StartLoggingAsync(string path) => throw new NotSupportedException();

        public Task StopLoggingAsync() => throw new NotSupportedException();
    }
}
