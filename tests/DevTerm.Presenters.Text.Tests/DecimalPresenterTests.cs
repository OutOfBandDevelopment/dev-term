using DevTerm.Test.Utilities;
using static DevTerm.Presenters.Text.Tests.TestSequence;

namespace DevTerm.Presenters.Text.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class DecimalPresenterTests
{
    private readonly DecimalPresenter _presenter = new();

    [TestMethod]
    public void Name_IsDecimal() => Assert.AreEqual("decimal", _presenter.Name);

    [TestMethod]
    public void Render_SpaceSeparatesEachByte() =>
        Assert.AreEqual("0 128 255", _presenter.Render(Of(0, 128, 255)).Single());

    [TestMethod]
    public void Parse_SplitsOnSpaces() =>
        Assert.AreSequenceEqual(new byte[] { 0, 128, 255 }, _presenter.Parse("0 128 255"));

    [TestMethod]
    public void RoundTrip_RenderThenParse_ReturnsOriginalBytes()
    {
        byte[] original = [1, 2, 3, 254];

        var result = _presenter.Parse(_presenter.Render(Of(original)).Single());

        Assert.AreSequenceEqual(original, result);
    }
}
