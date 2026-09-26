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
        // "25:00:00.000". See docs/bugs/fixed/040-playback-offset-over-24h.md.
        var offset = TimeSpan.FromHours(25);

        var text = PlaybackText.FormatOffset(offset);

        Assert.AreEqual("25:00:00.000", text);
    }

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
