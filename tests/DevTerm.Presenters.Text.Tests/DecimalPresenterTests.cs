namespace DevTerm.Presenters.Text.Tests;

[TestClass]
public sealed class DecimalPresenterTests
{
    private readonly DecimalPresenter _presenter = new();

    [TestMethod]
    public void Name_IsDecimal() => Assert.AreEqual("decimal", _presenter.Name);

    [TestMethod]
    public void Render_SpaceSeparatesEachByte() =>
        Assert.AreEqual("0 128 255", _presenter.Render(new byte[] { 0, 128, 255 }));

    [TestMethod]
    public void Parse_SplitsOnSpaces() =>
        CollectionAssert.AreEqual(new byte[] { 0, 128, 255 }, _presenter.Parse("0 128 255"));

    [TestMethod]
    public void RoundTrip_RenderThenParse_ReturnsOriginalBytes()
    {
        byte[] original = [1, 2, 3, 254];

        var result = _presenter.Parse(_presenter.Render(original));

        CollectionAssert.AreEqual(original, result);
    }
}
