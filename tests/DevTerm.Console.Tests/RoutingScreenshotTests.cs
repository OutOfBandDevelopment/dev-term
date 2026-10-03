using DevTerm.Configuration;
using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>The real TUI screenshot for docs/user-guide/routing.md: <see cref="RoutingMode"/> with a connected broker, two rules and some traffic.</summary>
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
    public async Task Routing_WithTraffic_IsCaptured()
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

        var router = tab.Routing.Router!;
        foreach (var line in new[] { "T=21", "T=22", "hello" })
        {
            router.Render(new System.Buffers.ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(line + "\r\n")));
        }

        var dump = "";
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = RoutingMode.BuildWindow(app, new RoutingViewModel(tab));
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                parts.Sample.Text = "T=21";
                parts.RunTest();
                app.LayoutAndDraw(true);
                dump = TuiTestRunner.DumpBuffer();
                var images = Path.Combine(FindRepoRoot(), "docs", "user-guide", "images");
                Directory.CreateDirectory(images);
                TuiScreenshot.Save(Path.Combine(images, "tui-routing.png"));
            }
            finally
            {
                app.End(token);
                parts.Window.Dispose();
            }
        });

        File.WriteAllText(Path.Combine(FindRepoRoot(), "docs", "user-guide", "images", "tui-routing.txt"), System.Text.RegularExpressions.Regex.Replace(dump, @"\d\d:\d\d:\d\d", "12:00:00"));
        await tab.Session.CloseAsync();

        Assert.Contains("Broker: Connected", dump);
        Assert.Contains("Unmatched: 1", dump);
        Assert.Contains("bench/temp", dump);
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
