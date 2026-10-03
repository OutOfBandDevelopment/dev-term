using DevTerm.Core.Routing;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class RoutingServiceTests
{
    private static RoutingOptions Options() => new()
    {
        Host = "broker",
        Rules = [new RoutingRule { Direction = RoutingDirection.DeviceToBroker, Match = @"^T=(?<t>\d+)$", Topic = "temp/${t}" }],
    };

    private static Core.Sessions.Session NewSession() =>
        DevTermSessionBuilder.Build(new CliOptions { Transport = "loopback" }).Session;

    private static void Feed(RoutingService service, string line) =>
        service.Router!.Render(new System.Buffers.ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(line + "\r\n")));

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition(), "Timed out waiting for the condition.");
    }

    [TestMethod]
    public async Task Start_ConnectsInTheBackground_AndPublishesThroughTheLink()
    {
        var factory = new FakeFactory();
        await using var service = new RoutingService(Options(), factory);
        service.Start(NewSession());
        await WaitAsync(() => service.State == RoutingState.Connected);

        Feed(service, "T=21");
        await WaitAsync(() => factory.Links[0].Published.Count == 1);
        Assert.AreEqual("temp/21", factory.Links[0].Published[0]);
    }

    [TestMethod]
    public async Task Start_WithAnInvalidRule_FailsWithoutThrowing()
    {
        var options = Options();
        options.Rules[0].Match = "(";
        await using var service = new RoutingService(options, new FakeFactory());
        service.Start(NewSession());
        Assert.AreEqual(RoutingState.Failed, service.State);
        Assert.IsNotNull(service.Reason);
    }

    [TestMethod]
    public async Task ABrokerThatIsDown_RetriesWithBackoff_AndConnectsWhenItReturns()
    {
        var factory = new FakeFactory { FailStarts = 2 };
        await using var service = new RoutingService(Options(), factory) { Backoff = [TimeSpan.FromMilliseconds(10)] };
        service.Start(NewSession());
        await WaitAsync(() => service.State == RoutingState.Connected);
        Assert.AreEqual(3, factory.Links.Count);
    }

    [TestMethod]
    public async Task ALostConnection_Reconnects_AndPublishesDuringTheOutageAreCounted()
    {
        var factory = new FakeFactory { FailStarts = 0 };
        await using var service = new RoutingService(Options(), factory) { Backoff = [TimeSpan.FromMilliseconds(200)] };
        service.Start(NewSession());
        await WaitAsync(() => service.State == RoutingState.Connected);

        factory.Links[0].RaiseLost();
        await WaitAsync(() => service.State == RoutingState.Reconnecting);
        Feed(service, "T=5");
        await WaitAsync(() => service.DroppedPublishes == 1);
        await WaitAsync(() => service.State == RoutingState.Connected);
        Assert.AreEqual(2, factory.Links.Count);
    }

    [TestMethod]
    public async Task Stop_ReturnsToStopped_AndDisposesTheLink()
    {
        var factory = new FakeFactory();
        var service = new RoutingService(Options(), factory);
        service.Start(NewSession());
        await WaitAsync(() => service.State == RoutingState.Connected);
        await service.StopAsync();
        Assert.AreEqual(RoutingState.Stopped, service.State);
        Assert.IsTrue(factory.Links[0].Disposed);
    }

    [TestMethod]
    public void Profile_RoundTripsRules_AndTheEnvironmentOverridesThePassword()
    {
        var options = new CliOptions { Transport = "loopback", Routing = Options() };
        options.Routing.Password = "DevPass1";
        var json = DevTermConfiguration.ToProfileJson(options);
        StringAssert.Contains(json, "\"Routing\"");

        var file = Path.GetTempFileName();
        Environment.SetEnvironmentVariable("DEVTERM_ROUTING__PASSWORD", "FromEnv");
        try
        {
            File.WriteAllText(file, json);
            var configuration = new ConfigurationBuilder().AddJsonFile(file).AddEnvironmentVariables("DEVTERM_").Build();
            var bound = new CliOptions();
            DevTermConfiguration.Bind(configuration, bound);
            Assert.AreEqual("broker", bound.Routing.Host);
            Assert.HasCount(1, bound.Routing.Rules);
            Assert.AreEqual(RoutingDirection.DeviceToBroker, bound.Routing.Rules[0].Direction);
            Assert.AreEqual("FromEnv", bound.Routing.Password);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEVTERM_ROUTING__PASSWORD", null);
            File.Delete(file);
        }
    }

    [TestMethod]
    public void Profile_WithoutRules_OmitsTheRoutingSection()
    {
        var json = DevTermConfiguration.ToProfileJson(new CliOptions { Transport = "loopback" });
        Assert.IsFalse(json.Contains("Routing", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ASessionTab_StartsRoutingWhenItsSessionOpens_AndStopsWhenItCloses()
    {
        var options = new CliOptions { Transport = "loopback", Routing = Options() };
        var tab = SessionTab.Build(options);
        tab.RoutingLinkFactory = new FakeFactory();

        await tab.Session.OpenAsync();
        await WaitAsync(() => tab.Routing.State == RoutingState.Connected);

        await tab.Session.CloseAsync();
        await WaitAsync(() => tab.Routing.State == RoutingState.Stopped);
    }

    [TestMethod]
    public async Task ASessionTab_WithoutRules_NeverStartsRouting()
    {
        var tab = SessionTab.Build(new CliOptions { Transport = "loopback" });
        await tab.Session.OpenAsync();
        Assert.AreEqual(RoutingState.Stopped, tab.Routing.State);
        await tab.Session.CloseAsync();
    }

    private sealed class FakeFactory : IRoutingLinkFactory
    {
        public List<FakeLink> Links { get; } = [];

        public int FailStarts { get; set; }

        public IRoutingLink Create(RoutingOptions options)
        {
            var link = new FakeLink { Fail = Links.Count < FailStarts };
            Links.Add(link);
            return link;
        }
    }

    private sealed class FakeLink : IRoutingLink
    {
        public bool Fail { get; init; }

        public bool Disposed { get; private set; }

        public List<string> Published { get; } = [];

        public event Action<Exception?>? Lost;

        public void RaiseLost() => Lost?.Invoke(null);

        public Task StartAsync(MessageRouter router, CancellationToken cancellationToken) =>
            Fail ? Task.FromException(new IOException("down")) : Task.CompletedTask;

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Published.Add(topic);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
