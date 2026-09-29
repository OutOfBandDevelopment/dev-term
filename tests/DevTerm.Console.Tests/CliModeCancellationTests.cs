using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>
/// Regression coverage for bug 059: Ctrl+C must interrupt the CLI's pending stdin read even when
/// the injected <see cref="TextReader"/> doesn't itself observe the cancellation token once a read
/// has started - true of the real <see cref="System.Console.In"/> on Linux/macOS (a SyncTextReader
/// checks the token once, then blocks synchronously; confirmed against a real Linux kernel - see
/// docs/bugs/fixed/059-cli-ctrl-c-non-windows.md).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class CliModeCancellationTests
{
    public required TestContext TestContext { get; set; }

    /// <summary>Mirrors Console.In's real defect: once called, ReadLineAsync never observes the
    /// token - only a real line (or EOF) completes it, exactly like a SyncTextReader mid-read.</summary>
    private sealed class UncancellableStdin : TextReader
    {
        private readonly TaskCompletionSource<string?> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => new(_pending.Task);

        public void Type(string? line) => _pending.TrySetResult(line);
    }

    private async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for: {what}");
            }

            await Task.Delay(10, TestContext.CancellationToken);
        }
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task CtrlC_WhileStdinNeverObservesCancellation_StillExitsPromptly()
    {
        var transport = new FakeTransport();
        var ascii = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
        var catalog = new PresenterCatalog([ascii]);
        var session = new Session(transport, new Pipeline([ascii]));
        var options = new CliOptions { Transport = "loopback", Presenter = ["ascii"], Parser = "ascii" };
        var stdin = new UncancellableStdin();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var shutdownRequested = new CancellationTokenSource();

        var run = CliMode.RunAsync(session, catalog, options, stdin, stdout, stderr, shutdownRequested);
        await WaitUntilAsync(() => transport.OpenCount == 1, "the initial connect, so the loop has reached its pending stdin read");

        // Simulates Ctrl+C: the loop's own wait must react to this even though stdin's pending
        // ReadLineAsync task (mirroring Console.In's real behavior) never itself completes.
        shutdownRequested.Cancel();

        var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreEqual(0, exitCode);
    }
}
