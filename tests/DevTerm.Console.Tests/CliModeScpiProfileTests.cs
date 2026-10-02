using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Devices.Scpi;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>
/// <c>--scpiprofile</c> applies the profile's terminator to the <c>scpi</c> presenter in the CLI, so a
/// terminatorless instrument (the Rigol DS1102E) prints its reply instead of buffering forever.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class CliModeScpiProfileTests
{
    public required TestContext TestContext { get; set; }

    /// <summary>A stdin that never produces a line, so the loop runs until cancelled.</summary>
    private sealed class BlockingStdin : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }

    private async Task<string> RunAsync(string? scpiProfile)
    {
        var transport = new FakeTransport();
        var scpi = new ScpiReplyPresenter();
        var session = new Session(transport, new Pipeline([scpi]));
        var catalog = new PresenterCatalog([scpi, new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()))]);
        var options = new CliOptions { Transport = "loopback", Presenter = ["scpi"], Parser = "ascii", ScpiProfile = scpiProfile };
        var stdout = new StringWriter();
        using var cts = new CancellationTokenSource();
        var run = CliMode.RunAsync(session, catalog, options, new BlockingStdin(), stdout, new StringWriter(), cts);
        while (!stdout.ToString().Contains("Connected to", StringComparison.Ordinal))
        {
            await Task.Delay(10, TestContext.CancellationToken);
        }

        await transport.PushIncomingAsync(Encoding.ASCII.GetBytes("Rigol Technologies,DS1102E"));
        await Task.Delay(300, TestContext.CancellationToken);
        await cts.CancelAsync();
        await run;
        return stdout.ToString();
    }

    [TestMethod]
    public async Task TerminatorlessProfile_PrintsAReplyWithNoLineEnding()
    {
        var output = await RunAsync("Rigol DS1102E Oscilloscope");

        StringAssert.Contains(output, "[scpi] Rigol Technologies,DS1102E");
    }

    [TestMethod]
    public async Task NoProfile_StillWaitsForALineEnding()
    {
        var output = await RunAsync(null);

        Assert.DoesNotContain("[scpi]", output);
    }
}
