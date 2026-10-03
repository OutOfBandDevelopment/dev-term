using System.Buffers;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Plugins.KeyValue;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.Plugins;

/// <summary>The optional structured-message model (<see cref="StructuredMessage"/>), via the example <see cref="KeyValuePresenter"/>.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class KeyValuePresenterTests
{
    private static ReadOnlySequence<byte> Bytes(string text) => new(Encoding.ASCII.GetBytes(text));

    [TestMethod]
    public void Parse_ReadsNamesValuesAndUnits()
    {
        var message = KeyValuePresenter.Parse("temp=21.50 C volts=3.30 V state=OK");

        Assert.IsNotNull(message);
        Assert.HasCount(3, message.Fields);
        Assert.AreEqual("C", message.Find("TEMP")?.Unit);
        Assert.AreEqual(3.3, message.Number("volts"));
        Assert.IsNull(message.Find("state")?.Unit);
        Assert.IsNull(message.Number("state"));
        Assert.AreEqual("temp=21.50 C volts=3.30 V state=OK", message.ToString());
    }

    [TestMethod]
    public void Parse_ALineWithNoNameValueToken_IsNull() => Assert.IsNull(KeyValuePresenter.Parse("hello there"));

    [TestMethod]
    public void Render_BuffersUntilALineEnds_AndRaisesOneMessagePerLine()
    {
        var presenter = new KeyValuePresenter();
        var messages = new List<StructuredMessage>();
        presenter.MessageDecoded += (_, m) => messages.Add(m);

        Assert.IsEmpty(presenter.Render(Bytes("temp=1.5 ")));
        var rows = presenter.Render(Bytes("C\r\nnoise\r\n"));

        Assert.AreSequenceEqual(["temp = 1.5 C"], [.. rows]);
        Assert.HasCount(1, messages);
    }

    [TestMethod]
    public void Pipeline_RelaysAPresentersMessages_UntilItIsRemoved()
    {
        var presenter = new KeyValuePresenter();
        var pipeline = new Pipeline([]);
        var count = 0;
        pipeline.MessageDecoded += (_, _) => count++;

        pipeline.AddPresenter(presenter);
        _ = pipeline.Render(Bytes("a=1\n"));
        pipeline.RemovePresenter(presenter);
        _ = presenter.Render(Bytes("a=2\n"));

        Assert.AreEqual(1, count);
    }
}
