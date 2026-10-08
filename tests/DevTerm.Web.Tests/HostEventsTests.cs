using DevTerm.Test.Utilities;

namespace DevTerm.Web.Tests;

[TestClass]
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Web)]
public class HostEventsTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Publish_ReachesEverySubscriber_AsAnSseFrame()
    {
        var events = new HostEvents();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var first = events.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var second = events.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var firstRead = first.MoveNextAsync().AsTask();
        var secondRead = second.MoveNextAsync().AsTask();
        while (events.SubscriberCount < 2)
        {
            await Task.Delay(5, TestContext.CancellationToken);
        }

        events.Publish("connection-opened", new { id = "a1", name = "Scope" });

        Assert.IsTrue(await firstRead);
        Assert.IsTrue(await secondRead);
        Assert.AreEqual("event: connection-opened\ndata: {\"id\":\"a1\",\"name\":\"Scope\"}\n\n", first.Current);
        Assert.AreEqual(first.Current, second.Current);
        await cts.CancelAsync();
        await first.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
        await second.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
    }

    [TestMethod]
    public async Task ASubscriberThatLeaves_IsDropped()
    {
        var events = new HostEvents();
        using var cts = new CancellationTokenSource();
        var enumerator = events.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var pending = enumerator.MoveNextAsync().AsTask();
        while (events.SubscriberCount < 1)
        {
            await Task.Delay(5, TestContext.CancellationToken);
        }

        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        await enumerator.DisposeAsync();
        Assert.AreEqual(0, events.SubscriberCount);
    }
}
