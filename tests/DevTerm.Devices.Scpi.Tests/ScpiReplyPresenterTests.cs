using System.Buffers;
using System.Text;

namespace DevTerm.Devices.Scpi.Tests;

/// <summary>
/// Verifies <see cref="ScpiReplyPresenter"/>'s line-buffering, FIFO query/reply correlation, and its
/// fallback behavior for an unsolicited line (no pending query registered). No real instrument
/// involved, so UNIT.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class ScpiReplyPresenterTests
{
    private static ReadOnlySequence<byte> Bytes(string text) => new(Encoding.ASCII.GetBytes(text));

    [TestMethod]
    public void Render_NoNewlineYet_BuffersAndEmitsNothing()
    {
        var presenter = new ScpiReplyPresenter();

        var lines = presenter.Render(Bytes("partial"));

        Assert.IsEmpty(lines);
    }

    [TestMethod]
    public void Render_CompleteLfLine_EmitsTheLine()
    {
        var presenter = new ScpiReplyPresenter();

        var lines = presenter.Render(Bytes("HELLO\n"));

        CollectionAssert.AreEqual(new[] { "HELLO" }, lines.ToArray());
    }

    [TestMethod]
    public void Render_CrLfLine_TrimsTrailingCr()
    {
        var presenter = new ScpiReplyPresenter();

        var lines = presenter.Render(Bytes("HELLO\r\n"));

        CollectionAssert.AreEqual(new[] { "HELLO" }, lines.ToArray());
    }

    [TestMethod]
    public void Render_TwoLinesInOneChunk_EmitsBoth()
    {
        var presenter = new ScpiReplyPresenter();

        var lines = presenter.Render(Bytes("ONE\nTWO\n"));

        CollectionAssert.AreEqual(new[] { "ONE", "TWO" }, lines.ToArray());
    }

    [TestMethod]
    public void Render_SplitAcrossTwoChunks_EmitsOnlyOnceComplete()
    {
        var presenter = new ScpiReplyPresenter();

        var first = presenter.Render(Bytes("HEL"));
        var second = presenter.Render(Bytes("LO\n"));

        Assert.IsEmpty(first);
        CollectionAssert.AreEqual(new[] { "HELLO" }, second.ToArray());
    }

    [TestMethod]
    public void Render_UnsolicitedLine_StillEmitsTextButRaisesNoValuesChanged()
    {
        var presenter = new ScpiReplyPresenter();
        var raised = false;
        presenter.ValuesChanged += (_, _) => raised = true;

        var lines = presenter.Render(Bytes("UNSOLICITED\n"));

        CollectionAssert.AreEqual(new[] { "UNSOLICITED" }, lines.ToArray());
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Render_AfterQuerySent_CorrelatesNextLineAndRaisesValuesChanged()
    {
        var presenter = new ScpiReplyPresenter();
        IReadOnlyDictionary<string, string>? received = null;
        presenter.ValuesChanged += (_, values) => received = values;
        presenter.QuerySent("idn.reply");

        presenter.Render(Bytes("ACME,MODEL1,SN1,1.0\n"));

        Assert.IsNotNull(received);
        Assert.AreEqual("ACME,MODEL1,SN1,1.0", received!["idn.reply"]);
    }

    [TestMethod]
    public void Render_TwoQueriesThenTwoReplies_CorrelatesFifoOrder()
    {
        var presenter = new ScpiReplyPresenter();
        var received = new List<KeyValuePair<string, string>>();
        presenter.ValuesChanged += (_, values) => received.Add(values.Single());
        presenter.QuerySent("first.reply");
        presenter.QuerySent("second.reply");

        presenter.Render(Bytes("ONE\nTWO\n"));

        Assert.HasCount(2, received);
        Assert.AreEqual("first.reply", received[0].Key);
        Assert.AreEqual("ONE", received[0].Value);
        Assert.AreEqual("second.reply", received[1].Key);
        Assert.AreEqual("TWO", received[1].Value);
    }

    [TestMethod]
    public void Name_IsScpi()
    {
        var presenter = new ScpiReplyPresenter();

        Assert.AreEqual("scpi", presenter.Name);
    }
}
