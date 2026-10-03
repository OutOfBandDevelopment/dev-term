using System.IO;
using DevTerm.Configuration;
using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>The real WPF screenshot for docs/user-guide/routing.md: <see cref="RoutingWindow"/> with a connected broker, two rules and some traffic.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class RoutingScreenshotTests
{
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repo root (DevTerm.slnx).");
    }

    [TestMethod]
    public void RoutingWindow_WithTraffic_IsCaptured()
    {
        StaTestRunner.Run(async () =>
        {
            var options = new CliOptions { Transport = "loopback" };
            options.Routing.Host = "localhost";
            options.Routing.Rules =
            [
                new RoutingRule { Direction = RoutingDirection.DeviceToBroker, Match = @"^T=(?<t>\d+)$", Topic = "bench/temp", Payload = "${t}" },
                new RoutingRule { Direction = RoutingDirection.BrokerToDevice, Match = "(?<payload>.*)", Topic = "bench/cmd", Send = "SET ${payload}", Confirm = true },
            ];
            var tab = SessionTab.Build(options);
            tab.RoutingLinkFactory = new FakeLinkFactory();
            await tab.Session.OpenAsync();
            for (var i = 0; i < 100 && tab.Routing.State != RoutingState.Connected; i++)
            {
                await Task.Delay(10);
            }

            foreach (var line in new[] { "T=21", "T=22", "hello" })
            {
                tab.Routing.Router!.Render(new System.Buffers.ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(line + "\r\n")));
            }

            var window = new RoutingWindow(new RoutingViewModel(tab));
            WpfScreenshot.ShowOffScreen(window, 760, 760);
            window.SetSample("T=21");
            window.RunTest();
            StaTestRunner.DoEvents();
            window.UpdateLayout();

            Assert.AreEqual("Broker: Connected", window.StatusText);
            var path = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images", "wpf-routing.png");
            WpfScreenshot.Save(window, path);
            Assert.IsGreaterThan(1000L, new FileInfo(path).Length);
            window.Close();
            await tab.Session.CloseAsync();
        });
    }

    private sealed class FakeLinkFactory : IRoutingLinkFactory
    {
        public IRoutingLink Create(RoutingOptions options) => new FakeLink();
    }

    private sealed class FakeLink : IRoutingLink
    {
        public event Action<Exception?>? Lost
        {
            add { }
            remove { }
        }

        public Task StartAsync(MessageRouter router, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
