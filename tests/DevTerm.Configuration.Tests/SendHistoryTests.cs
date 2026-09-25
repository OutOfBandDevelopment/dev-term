using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class SendHistoryTests
{
    [TestMethod]
    public void Previous_WithNoHistory_ReturnsNull()
    {
        var history = new SendHistory();

        Assert.IsNull(history.Previous());
    }

    [TestMethod]
    public void Next_WithNoHistory_ReturnsNull()
    {
        var history = new SendHistory();

        Assert.IsNull(history.Next());
    }

    [TestMethod]
    public void Add_BlankLine_IsNotRecorded()
    {
        var history = new SendHistory();

        history.Add(string.Empty);

        Assert.IsEmpty(history.Items);
    }

    [TestMethod]
    public void Add_InsertsMostRecentFirst()
    {
        var history = new SendHistory();

        history.Add("first");
        history.Add("second");

        Assert.AreSequenceEqual(new[] { "second", "first" }, history.Items);
    }

    [TestMethod]
    public void Add_BeyondCapacity_TrimsOldestEntries()
    {
        var history = new SendHistory();

        for (var i = 0; i < SendHistory.Capacity + 10; i++)
        {
            history.Add($"line{i}");
        }

        Assert.HasCount(SendHistory.Capacity, history.Items);
        Assert.AreEqual($"line{SendHistory.Capacity + 9}", history.Items[0]);
    }

    [TestMethod]
    public void Previous_StepsFromNewestToOldest()
    {
        var history = new SendHistory();
        history.Add("first");
        history.Add("second");

        Assert.AreEqual("second", history.Previous());
        Assert.AreEqual("first", history.Previous());
    }

    [TestMethod]
    public void Previous_PastOldestEntry_ReturnsNull()
    {
        var history = new SendHistory();
        history.Add("only");

        Assert.AreEqual("only", history.Previous());
        Assert.IsNull(history.Previous());
    }

    [TestMethod]
    public void Next_AfterPrevious_StepsBackTowardNewest()
    {
        var history = new SendHistory();
        history.Add("first");
        history.Add("second");

        history.Previous();
        history.Previous();

        Assert.AreEqual("second", history.Next());
    }

    [TestMethod]
    public void Next_PastNewestEntry_ReturnsEmptyStringOnce()
    {
        var history = new SendHistory();
        history.Add("only");

        history.Previous();

        Assert.AreEqual(string.Empty, history.Next());
    }

    [TestMethod]
    public void Next_AlreadyPastNewest_ReturnsNull()
    {
        var history = new SendHistory();
        history.Add("only");

        history.Previous();
        history.Next();

        Assert.IsNull(history.Next());
    }

    [TestMethod]
    public void Add_ResetsCursorBackToNewest()
    {
        var history = new SendHistory();
        history.Add("first");
        history.Previous();

        history.Add("second");

        Assert.AreEqual("second", history.Previous());
    }

    [TestMethod]
    public void ResetCursor_DoesNotTouchRecordedHistory()
    {
        var history = new SendHistory();
        history.Add("first");
        history.Previous();

        history.ResetCursor();

        Assert.AreEqual("first", history.Previous());
        Assert.AreSequenceEqual(new[] { "first" }, history.Items);
    }
}
