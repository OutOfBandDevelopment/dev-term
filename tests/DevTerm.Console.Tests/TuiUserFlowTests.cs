using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Terminal.Gui.Input;

namespace DevTerm.Console.Tests;

/// <summary>The shared user flows, driven through the real Terminal.Gui main window (headless driver, running app loop).</summary>
[TestClass]
[TestCategory(TestCategories.Unit)]
[DoNotParallelize]
public sealed class TuiUserFlowTests : UserFlowTestsBase
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    protected override async Task RunAsync(Func<IFrontEndDriver, Task> flow)
    {
        var cliOptions = new CliOptions { Transport = "loopback", Presenter = ["ascii"], Parser = "ascii" };
        var built = DevTermSessionBuilder.Build(cliOptions);
        await built.Session.OpenAsync(TestContext.CancellationToken);

        Exception? failure = null;
        TuiTestRunner.RunWithLoop(built.Session, built.Catalog, cliOptions, parts =>
        {
            try
            {
                flow(new Driver(parts)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        await built.Session.DisposeAsync();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class Driver(TuiWindowParts parts) : IFrontEndDriver
    {
        public string Name => "TUI";

        public bool CanDisconnect => true;

        public Task SendAsync(string line)
        {
            TuiTestRunner.InvokeOnLoop(() =>
            {
                parts.SendField.Text = line;
                parts.SendField.SetFocus();
                return TuiTestRunner.CurrentApp.Keyboard.RaiseKeyDownEvent(Key.Enter);
            });
            return Task.CompletedTask;
        }

        public Task<string> OutputAsync() => Task.FromResult(TuiTestRunner.InvokeOnLoop(() => parts.Output.Text));

        public Task<bool> WaitForOutputAsync(string text, TimeSpan timeout) =>
            Task.FromResult(TuiTestRunner.WaitUntilOnLoop(() => parts.Output.Text.Contains(text, StringComparison.Ordinal), timeout));

        public Task<bool> IsConnectedAsync() =>
            Task.FromResult(TuiTestRunner.InvokeOnLoop(() => parts.StatusLabel.Text.Contains("● Connected", StringComparison.Ordinal)));

        public Task DisconnectAsync() => ToggleAsync("_Connect");

        public Task ReconnectAsync() => ToggleAsync("_Disconnect");

        private Task ToggleAsync(string expectedTitle)
        {
            parts.ToggleConnectionAsync().GetAwaiter().GetResult();
            Assert.IsTrue(TuiTestRunner.WaitUntilOnLoop(() => parts.ConnectMenuItem.Title == expectedTitle, _wait), $"Expected the menu item to read {expectedTitle}.");
            return Task.CompletedTask;
        }
    }
}
