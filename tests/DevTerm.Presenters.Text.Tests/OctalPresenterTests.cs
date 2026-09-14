using static DevTerm.Presenters.Text.Tests.TestSequence;

namespace DevTerm.Presenters.Text.Tests;

[TestClass]
public sealed class OctalPresenterTests
{
    private readonly OctalPresenter _presenter = new();

    [TestMethod]
    public void Name_IsOctal() => Assert.AreEqual("octal", _presenter.Name);

    [TestMethod]
    public void Render_PadsEachByteToThreeDigits() =>
        Assert.AreEqual("000 007 377", _presenter.Render(Of(0, 7, 255)));

    [TestMethod]
    public void RoundTrip_RenderThenParse_ReturnsOriginalBytes()
    {
        byte[] original = [0, 7, 255, 64];

        var result = _presenter.Parse(_presenter.Render(Of(original)));

        CollectionAssert.AreEqual(original, result);
    }
}
