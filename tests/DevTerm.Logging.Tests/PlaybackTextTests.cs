using DevTerm.Logging.Playback;
using DevTerm.Test.Utilities;

namespace DevTerm.Logging.Tests;

/// <summary>Formatting of playback offsets and escaped sent bytes - see <see cref="PlaybackText"/>.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Logging)]
[TestClass]
public sealed class PlaybackTextTests
{
    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void FormatOffset_PastTwentyFourHours_DoesNotWrap()
    {
        // Regression test for bug 040: TimeSpan's "h" custom format specifier is the hour-of-day
        // component (0-23), not total hours, so a 25-hour offset showed as "1:00:00.000" instead of
        // "25:00:00.000". See docs/bugs/resolved/040-playback-offset-over-24h.md.
        var offset = TimeSpan.FromHours(25);

        var text = PlaybackText.FormatOffset(offset);

        Assert.AreEqual("25:00:00.000", text);
    }

    [TestMethod]
    [DataRow("120", 120, null)]
    [DataRow("  7 ", 7, null)]
    [DataRow("1:23", null, 83000)]
    [DataRow("0:02.5", null, 2500)]
    [DataRow("1:02:03.250", null, 3723250)]
    public void TryParseJump_ReadsRecordNumbersAndTimes(string text, int? record, int? timeMs)
    {
        Assert.IsTrue(PlaybackText.TryParseJump(text, out var parsedRecord, out var parsedTime));
        Assert.AreEqual(record, parsedRecord);
        Assert.AreEqual(timeMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null, parsedTime);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("-3")]
    [DataRow("abc")]
    [DataRow("1:75")]
    [DataRow("1:2:3:4")]
    [DataRow("1:61:00")]
    [DataRow("x:10")]
    public void TryParseJump_RejectsAnythingElse(string text) =>
        Assert.IsFalse(PlaybackText.TryParseJump(text, out _, out _));

    [TestMethod]
    public void FormatOffset_UnderOneHour_UsesMinutesAndSeconds()
    {
        var text = PlaybackText.FormatOffset(new TimeSpan(0, 0, 1, 2, 300));

        Assert.AreEqual("01:02.300", text);
    }

    [TestMethod]
    public void FormatOffset_ExactlyOneHour_UsesHourFormat()
    {
        var text = PlaybackText.FormatOffset(TimeSpan.FromHours(1));

        Assert.AreEqual("1:00:00.000", text);
    }
}
