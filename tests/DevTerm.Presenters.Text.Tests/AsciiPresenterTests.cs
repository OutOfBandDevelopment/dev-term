using Microsoft.Extensions.Options;
using static DevTerm.Presenters.Text.Tests.TestSequence;

namespace DevTerm.Presenters.Text.Tests;

[TestCategory("UNIT")]
[TestClass]
public sealed class AsciiPresenterTests
{
    private readonly AsciiPresenter _presenter = Create();

    private static AsciiPresenter Create(int maxLineLength = AsciiPresenter.DefaultMaxLineLength) =>
        new(Options.Create(new AsciiPresenterOptions { MaxLineLength = maxLineLength }));

    [TestMethod]
    public void Name_IsAscii() => Assert.AreEqual("ascii", _presenter.Name);

    [TestMethod]
    public void Parse_EncodesAsciiBytes() =>
        Assert.AreSequenceEqual(new byte[] { 0x48, 0x69, 0x21 }, _presenter.Parse("Hi!"));

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

        Assert.AreSequenceEqual(new[] { "Hi!" }, result.ToArray());
    }

    [TestMethod]
    public void Render_WithCarriageReturn_FlushesTheAccumulatedLine()
    {
        var result = _presenter.Render(Of("Hi!\r"u8.ToArray()));

        Assert.AreSequenceEqual(new[] { "Hi!" }, result.ToArray());
    }

    [TestMethod]
    public void Render_WithCarriageReturnLineFeed_FlushesOnlyOnce()
    {
        var result = _presenter.Render(Of("Hi!\r\n"u8.ToArray()));

        Assert.AreSequenceEqual(new[] { "Hi!" }, result.ToArray());
    }

    [TestMethod]
    public void Render_CrLfSplitAcrossTwoCalls_StillFlushesOnlyOnce()
    {
        var first = _presenter.Render(Of("Hi!\r"u8.ToArray()));
        var second = _presenter.Render(Of("\nNext"u8.ToArray()));

        Assert.AreSequenceEqual(new[] { "Hi!" }, first.ToArray());
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
        Assert.AreSequenceEqual(new[] { "Hi" }, afterTerminator.ToArray());
    }

    [TestMethod]
    public void Render_MultipleLinesInOneChunk_ReturnsEachLineSeparately()
    {
        var result = _presenter.Render(Of("one\ntwo\n"u8.ToArray()));

        Assert.AreSequenceEqual(new[] { "one", "two" }, result.ToArray());
    }

    [TestMethod]
    public void Render_MaxLineLengthReached_FlushesWithoutATerminator()
    {
        var presenter = Create(maxLineLength: 3);

        var result = presenter.Render(Of("abcdef"u8.ToArray()));

        Assert.AreSequenceEqual(new[] { "abc", "def" }, result.ToArray());
    }

    [TestMethod]
    public void Render_MaxLineLengthZero_IsUnboundedAndWaitsForTerminator()
    {
        var presenter = Create(maxLineLength: 0);

        var longRun = presenter.Render(Of(new string('a', 10_000).Select(c => (byte)c).ToArray()));
        Assert.IsEmpty(longRun, "Length alone should never flush when MaxLineLength is 0.");

        var afterTerminator = presenter.Render(Of((byte)'\n'));
        Assert.HasCount(1, afterTerminator);
        Assert.AreEqual(10_000, afterTerminator[0].Length);
    }

    [TestMethod]
    public void Constructor_NegativeMaxLineLength_Throws() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Create(maxLineLength: -1));

    [TestMethod]
    public void RoundTrip_ParseThenRenderWithTerminator_ReturnsOriginalText()
    {
        const string original = "Hello, dev-term!";

        var bytes = _presenter.Parse(original);
        var result = _presenter.Render(Of([.. bytes, (byte)'\n']));

        Assert.AreSequenceEqual(new[] { original }, result.ToArray());
    }
}
