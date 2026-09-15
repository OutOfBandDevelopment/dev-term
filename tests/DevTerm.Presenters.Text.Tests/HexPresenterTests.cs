using static DevTerm.Presenters.Text.Tests.TestSequence;

namespace DevTerm.Presenters.Text.Tests;

[TestCategory("UNIT")]
[TestClass]
public sealed class HexPresenterTests
{
    private readonly HexPresenter _presenter = new();

    [TestMethod]
    public void Name_IsHex() => Assert.AreEqual("hex", _presenter.Name);

    [TestMethod]
    public void Render_UppercasesAndConcatenatesWithoutSeparators() =>
        Assert.AreEqual("FF00A5", _presenter.Render(Of(0xFF, 0x00, 0xA5)).Single());

    [TestMethod]
    public void Parse_AcceptsUnprefixedHex() =>
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x00, 0xA5 }, _presenter.Parse("FF00A5"));

    [TestMethod]
    public void Parse_AcceptsSpacedAndPrefixedHex() =>
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x00, 0xA5 }, _presenter.Parse("0xFF 0x00 0xA5"));

    [TestMethod]
    public void Parse_IsCaseInsensitiveForThe0xPrefix() =>
        CollectionAssert.AreEqual(new byte[] { 0xAB }, _presenter.Parse("0XAB"));
}
