using static DevTerm.Presenters.Text.Tests.TestSequence;

namespace DevTerm.Presenters.Text.Tests;

[TestClass]
public sealed class AsciiPresenterTests
{
    private readonly AsciiPresenter _presenter = new();

    [TestMethod]
    public void Name_IsAscii() => Assert.AreEqual("ascii", _presenter.Name);

    [TestMethod]
    public void Parse_EncodesAsciiBytes() =>
        CollectionAssert.AreEqual(new byte[] { 0x48, 0x69, 0x21 }, _presenter.Parse("Hi!"));

    [TestMethod]
    public void Render_WithoutATerminator_BuffersAndReturnsNothingYet()
    {
        var result = _presenter.Render(Of(0x48, 0x69)); // "Hi", no CR/LF

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void Render_WithLineFeed_FlushesTheAccumulatedLine()
    {
        var result = _presenter.Render(Of("Hi!\n"u8.ToArray()));

        CollectionAssert.AreEqual(new[] { "Hi!" }, result.ToArray());
    }

    [TestMethod]
    public void Render_WithCarriageReturn_FlushesTheAccumulatedLine()
    {
        var result = _presenter.Render(Of("Hi!\r"u8.ToArray()));

        CollectionAssert.AreEqual(new[] { "Hi!" }, result.ToArray());
    }

    [TestMethod]
    public void Render_WithCarriageReturnLineFeed_FlushesOnlyOnce()
    {
        var result = _presenter.Render(Of("Hi!\r\n"u8.ToArray()));

        CollectionAssert.AreEqual(new[] { "Hi!" }, result.ToArray());
    }

    [TestMethod]
    public void Render_CrLfSplitAcrossTwoCalls_StillFlushesOnlyOnce()
    {
        var first = _presenter.Render(Of("Hi!\r"u8.ToArray()));
        var second = _presenter.Render(Of("\nNext"u8.ToArray()));

        CollectionAssert.AreEqual(new[] { "Hi!" }, first.ToArray());
        Assert.IsEmpty(second, "The LF half of the CRLF pair should be swallowed, not start a new empty line.");
    }

    [TestMethod]
    public void Render_AccumulatesAcrossMultipleCallsUntilATerminatorArrives()
    {
        var afterH = _presenter.Render(Of((byte)'H'));
        var afterI = _presenter.Render(Of((byte)'i'));
        var afterTerminator = _presenter.Render(Of((byte)'\n'));

        Assert.IsEmpty(afterH);
        Assert.IsEmpty(afterI);
        CollectionAssert.AreEqual(new[] { "Hi" }, afterTerminator.ToArray());
    }

    [TestMethod]
    public void Render_MultipleLinesInOneChunk_ReturnsEachLineSeparately()
    {
        var result = _presenter.Render(Of("one\ntwo\n"u8.ToArray()));

        CollectionAssert.AreEqual(new[] { "one", "two" }, result.ToArray());
    }

    [TestMethod]
    public void Render_MaxLineLengthReached_FlushesWithoutATerminator()
    {
        var presenter = new AsciiPresenter(maxLineLength: 3);

        var result = presenter.Render(Of("abcdef"u8.ToArray()));

        CollectionAssert.AreEqual(new[] { "abc", "def" }, result.ToArray());
    }

    [TestMethod]
    public void Constructor_NonPositiveMaxLineLength_Throws() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AsciiPresenter(maxLineLength: 0));

    [TestMethod]
    public void RoundTrip_ParseThenRenderWithTerminator_ReturnsOriginalText()
    {
        const string original = "Hello, dev-term!";

        var bytes = _presenter.Parse(original);
        var result = _presenter.Render(Of([.. bytes, (byte)'\n']));

        CollectionAssert.AreEqual(new[] { original }, result.ToArray());
    }
}
