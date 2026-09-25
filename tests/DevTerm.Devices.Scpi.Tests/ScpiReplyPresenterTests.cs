using System.Buffers;
using System.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Devices.Scpi.Tests;

/// <summary>
/// Verifies <see cref="ScpiReplyPresenter"/>'s line-buffering, FIFO query/reply correlation, and its
/// fallback behavior for an unsolicited line (no pending query registered). No real instrument
/// involved, so UNIT.
/// </summary>
[TestCategory(TestCategories.Unit)]
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

        Assert.AreSequenceEqual(["HELLO"], [.. lines]);
    }

    [TestMethod]
    public void Render_CrLfLine_TrimsTrailingCr()
    {
        var presenter = new ScpiReplyPresenter();

        var lines = presenter.Render(Bytes("HELLO\r\n"));

        Assert.AreSequenceEqual(["HELLO"], [.. lines]);
    }

    [TestMethod]
    public void Render_BareCrLine_EmitsTheLine()
    {
        // Real-hardware-confirmed: a Tektronix TDS2024 over TCP terminates replies with a bare CR,
        // not LF — matches AsciiPresenter's CR/LF/CRLF-as-one-terminator handling. An LF-only
        // version of this presenter never completed the line, so a query's reply indicator never
        // updated even though the same bytes rendered fine via the ascii presenter.
        var presenter = new ScpiReplyPresenter();

        var lines = presenter.Render(Bytes("ID TEK/TDS 2024,CF:91.1CT,FV:v4.12 TDS2CM:CMV:v1.04\r"));

        Assert.AreSequenceEqual(["ID TEK/TDS 2024,CF:91.1CT,FV:v4.12 TDS2CM:CMV:v1.04"], [.. lines]);
    }

    [TestMethod]
    public void Render_TwoLinesInOneChunk_EmitsBoth()
    {
        var presenter = new ScpiReplyPresenter();

        var lines = presenter.Render(Bytes("ONE\nTWO\n"));

        Assert.AreSequenceEqual(["ONE", "TWO"], [.. lines]);
    }

    [TestMethod]
    public void Render_SplitAcrossTwoChunks_EmitsOnlyOnceComplete()
    {
        var presenter = new ScpiReplyPresenter();

        var first = presenter.Render(Bytes("HEL"));
        var second = presenter.Render(Bytes("LO\n"));

        Assert.IsEmpty(first);
        Assert.AreSequenceEqual(["HELLO"], [.. second]);
    }

    [TestMethod]
    public void Render_UnsolicitedLine_StillEmitsTextButRaisesNoValuesChanged()
    {
        var presenter = new ScpiReplyPresenter();
        var raised = false;
        presenter.ValuesChanged += (_, _) => raised = true;

        var lines = presenter.Render(Bytes("UNSOLICITED\n"));

        Assert.AreSequenceEqual(["UNSOLICITED"], [.. lines]);
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
    public void Render_Terminatorless_FlushesWhateverArrivedInOneChunkWithNoTerminator()
    {
        // Real-hardware-confirmed: a Korad KA3005P/KA6003P reply (e.g. "05.00" for VOUT1?) has no
        // terminator at all. Without ConfigureTerminator(""), this presenter buffers forever waiting
        // for a CR/LF that never arrives — a Korad control panel's reply indicators never updated,
        // making the whole panel look unresponsive even though the device replied correctly.
        var presenter = new ScpiReplyPresenter();
        presenter.ConfigureTerminator(string.Empty);

        var lines = presenter.Render(Bytes("05.00"));

        Assert.AreSequenceEqual(["05.00"], [.. lines]);
    }

    [TestMethod]
    public void Render_Terminatorless_StillCorrelatesWithPendingQuery()
    {
        var presenter = new ScpiReplyPresenter();
        presenter.ConfigureTerminator(string.Empty);
        IReadOnlyDictionary<string, string>? received = null;
        presenter.ValuesChanged += (_, values) => received = values;
        presenter.QuerySent("voutQuery.reply");

        presenter.Render(Bytes("05.00"));

        Assert.IsNotNull(received);
        Assert.AreEqual("05.00", received!["voutQuery.reply"]);
    }

    [TestMethod]
    public void Render_Terminatorless_EmptyChunkEmitsNothing()
    {
        var presenter = new ScpiReplyPresenter();
        presenter.ConfigureTerminator(string.Empty);

        var lines = presenter.Render(Bytes(string.Empty));

        Assert.IsEmpty(lines);
    }

    [TestMethod]
    public void Name_IsScpi()
    {
        var presenter = new ScpiReplyPresenter();

        Assert.AreEqual("scpi", presenter.Name);
    }
}
