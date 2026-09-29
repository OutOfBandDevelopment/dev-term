using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

/// <summary>See docs/bugs/fixed/031-tui-output-no-backpressure.md.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class BatchedOutputQueueTests
{
    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Enqueue_ABurstOfLinesBeforeTheDrainRuns_SchedulesOnlyOneDrain()
    {
        var scheduleCount = 0;
        var queue = new BatchedOutputQueue(() => scheduleCount++);

        for (var i = 0; i < 1000; i++)
        {
            queue.Enqueue($"line{i}");
        }

        Assert.AreEqual(1, scheduleCount, "A burst that arrives before the drain runs must not queue a separate UI update per line.");
    }

    [TestMethod]
    public void Drain_HandsEveryPendingLineToApplyInOneCall_InOrder()
    {
        var queue = new BatchedOutputQueue(() => { });
        queue.Enqueue("a");
        queue.Enqueue("b");
        queue.Enqueue("c");

        IReadOnlyList<string>? applied = null;
        queue.Drain(lines => applied = lines);

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, applied!.ToArray());
    }

    [TestMethod]
    public void Drain_WithNothingPending_DoesNotCallApply()
    {
        var queue = new BatchedOutputQueue(() => { });
        var called = false;

        queue.Drain(_ => called = true);

        Assert.IsFalse(called);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Enqueue_AfterADrainCompletes_SchedulesAFreshDrainForTheNextBurst()
    {
        var scheduleCount = 0;
        var queue = new BatchedOutputQueue(() => scheduleCount++);

        queue.Enqueue("a");
        queue.Drain(_ => { });
        queue.Enqueue("b");

        Assert.AreEqual(2, scheduleCount);
    }
}
