namespace DevTerm.Presenters.Text.Tests;

[TestClass]
public sealed class AsciiPresenterTests
{
    private readonly AsciiPresenter _presenter = new();

    [TestMethod]
    public void Name_IsAscii() => Assert.AreEqual("ascii", _presenter.Name);

    [TestMethod]
    public void Render_DecodesAsciiBytes() =>
        Assert.AreEqual("Hi!", _presenter.Render(new byte[] { 0x48, 0x69, 0x21 }));

    [TestMethod]
    public void Parse_EncodesAsciiBytes() =>
        CollectionAssert.AreEqual(new byte[] { 0x48, 0x69, 0x21 }, _presenter.Parse("Hi!"));

    [TestMethod]
    public void RoundTrip_ParseThenRender_ReturnsOriginalText()
    {
        const string original = "Hello, dev-term!";

        var result = _presenter.Render(_presenter.Parse(original));

        Assert.AreEqual(original, result);
    }
}
