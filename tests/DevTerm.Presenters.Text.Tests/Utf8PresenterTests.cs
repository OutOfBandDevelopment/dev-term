using System.Text;
using DevTerm.Test.Utilities;
using static DevTerm.Presenters.Text.Tests.TestSequence;

namespace DevTerm.Presenters.Text.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class Utf8PresenterTests
{
    private readonly Utf8Presenter _presenter = new();

    [TestMethod]
    public void Name_IsUtf8() => Assert.AreEqual("utf8", _presenter.Name);

    [TestMethod]
    public void RoundTrip_ParseThenRender_ReturnsOriginalText_IncludingMultiByteCharacters()
    {
        const string original = "café — 日本語";

        var result = _presenter.Render(Of(_presenter.Parse(original))).Single();

        Assert.AreEqual(original, result);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Render_MultiByteCharacterSplitAcrossTwoReads_DecodesCorrectlyOnceComplete()
    {
        var bytes = Encoding.UTF8.GetBytes("é");
        Assert.AreEqual(2, bytes.Length);

        var first = _presenter.Render(Of(bytes[..1]));
        var second = _presenter.Render(Of(bytes[1..]));

        Assert.IsEmpty(first);
        Assert.AreEqual("é", second.Single());
    }
}
