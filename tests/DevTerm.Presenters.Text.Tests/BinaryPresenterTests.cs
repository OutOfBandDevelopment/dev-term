using static DevTerm.Presenters.Text.Tests.TestSequence;

namespace DevTerm.Presenters.Text.Tests;

[TestClass]
public sealed class BinaryPresenterTests
{
    private readonly BinaryPresenter _presenter = new();

    [TestMethod]
    public void Name_IsBinary() => Assert.AreEqual("binary", _presenter.Name);

    [TestMethod]
    public void Render_PadsEachByteToEightBits() =>
        Assert.AreEqual("00000000 00000001 11111111", _presenter.Render(Of(0, 1, 255)));

    [TestMethod]
    public void RoundTrip_RenderThenParse_ReturnsOriginalBytes()
    {
        byte[] original = [0, 1, 255, 170];

        var result = _presenter.Parse(_presenter.Render(Of(original)));

        CollectionAssert.AreEqual(original, result);
    }
}
