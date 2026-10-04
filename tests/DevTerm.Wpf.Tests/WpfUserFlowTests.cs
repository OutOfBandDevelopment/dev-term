using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>The shared user flows, driven through the real, shown (off-screen) WPF main window.</summary>
[TestClass]
[TestCategory(TestCategories.Unit)]
[DoNotParallelize]
public sealed class WpfUserFlowTests : UserFlowTestsBase
{
    private static readonly TimeSpan _pump = TimeSpan.FromSeconds(10);

    protected override Task RunAsync(Func<IFrontEndDriver, Task> flow)
    {
        StaTestRunner.Run(async () =>
        {
            var cliOptions = new CliOptions { Transport = "loopback", Presenter = ["ascii"], Parser = "ascii" };
            var built = DevTermSessionBuilder.Build(cliOptions);
            var window = new MainWindow(built.Session, built.Catalog, cliOptions, IsolatedProfiles.Empty()) { ShowInTaskbar = false };
            WpfScreenshot.ShowOffScreen(window);
            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.ConnectionStatusText.Text.StartsWith("Connected", StringComparison.Ordinal), _pump), "The window never connected.");
            await flow(new Driver(window));
        });
        return Task.CompletedTask;
    }

    private sealed class Driver(MainWindow window) : IFrontEndDriver
    {
        public string Name => "WPF";

        public bool CanDisconnect => true;

        public async Task SendAsync(string line)
        {
            window.SendBox.Text = line;
            await window.SendCurrentInputAsync();
        }

        public Task<string> OutputAsync() => Task.FromResult(Text());

        public Task<bool> WaitForOutputAsync(string text, TimeSpan timeout) =>
            Task.FromResult(StaTestRunner.PumpUntil(() => Text().Contains(text, StringComparison.Ordinal), timeout));

        public Task<bool> IsConnectedAsync()
        {
            StaTestRunner.DoEvents();
            return Task.FromResult(window.ConnectionStatusText.Text.StartsWith("Connected", StringComparison.Ordinal));
        }

        public async Task DisconnectAsync()
        {
            await window.ToggleConnectionAsync();
            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.ConnectionStatusText.Text.StartsWith("Disconnected", StringComparison.Ordinal), _pump));
        }

        public async Task ReconnectAsync()
        {
            await window.ToggleConnectionAsync();
            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.ConnectionStatusText.Text.StartsWith("Connected", StringComparison.Ordinal), _pump));
        }

        private string Text() => string.Join(Environment.NewLine, window.OutputList.Items.Cast<object>().Select(i => i.ToString()));
    }
}
