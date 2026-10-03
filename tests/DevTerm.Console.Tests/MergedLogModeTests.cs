using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class MergedLogModeTests
{
    [TestMethod]
    public void Render_Empty_SaysNothingRecordedYet()
    {
        StringAssert.Contains(MergedLogMode.Render(new MergedSessionLog()), "no traffic yet");
    }
}
