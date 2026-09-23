using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.UiDefinitions;
using Moq;

namespace DevTerm.Devices.Scpi.Tests;

/// <summary>
/// End-to-end coverage against the real bundled <c>hp-agilent-keysight-34401a.json</c> profile
/// (loaded via <see cref="ScpiProfileCatalog.All"/>, not a synthetic inline profile like the other
/// test files in this project use) — the UI shape it produces, the exact bytes a Measure command
/// sends, and <see cref="ScpiReplyPresenter"/> decoding a realistic instrument reply. Added after a
/// real-hardware report that a 34401A's Measure buttons beeped the device but showed no reply in
/// either front end; nothing previously exercised this specific profile's actual command set, only
/// small hand-built profiles shaped similarly to it. No real instrument involved, so UNIT.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
public sealed class Hp34401aProfileTests
{
    private static ScpiInstrumentProfile Profile =>
        ScpiProfileCatalog.All.Single(p => p.Name.Contains("34401A", StringComparison.OrdinalIgnoreCase));

    private static (Session Session, Mock<ITransport> Transport) CreateSurfaceSession()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        return (session, transport);
    }

    private static void VerifySent(Mock<ITransport> transport, string expectedText)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expectedText);
        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(expectedBytes)),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public void Build_MeasureSection_HasAButtonAndReplyIndicatorForEachOfTheFiveQueries()
    {
        var definition = ScpiUiDefinitionBuilder.Build(Profile);
        var section = definition.Sections.Single(s => s.Label == "Measure");

        var queryIds = new[] { "measVoltDc", "measVoltAc", "measCurrDc", "measRes", "measFreq" };
        foreach (var id in queryIds)
        {
            Assert.IsNotNull(section.Controls.OfType<ButtonControl>().SingleOrDefault(c => c.Id == id), $"missing button for {id}");
            Assert.IsNotNull(section.Controls.OfType<IndicatorControl>().SingleOrDefault(c => c.Id == $"{id}.reply"), $"missing reply indicator for {id}");
        }

        // Every Measure control belongs to exactly the 5 buttons + 5 indicators above — no stray
        // extras from a parameter mis-shape, and no accidental Configure bleed-through.
        Assert.HasCount(10, section.Controls);
    }

    [TestMethod]
    public void Build_ConfigureSection_HasOneChoiceFieldAndASendButtonNoIndicator()
    {
        var definition = ScpiUiDefinitionBuilder.Build(Profile);
        var section = definition.Sections.Single(s => s.Label == "Configure");

        var field = (ChoiceControl)section.Controls.Single(c => c.Id == "confVoltDc.Range");
        var button = (ButtonControl)section.Controls.Single(c => c.Id == "confVoltDc.send");

        CollectionAssert.AreEqual(new[] { "AUTO", "0.1", "1", "10", "100", "1000" }, field.Options);
        Assert.AreEqual("AUTO", field.DefaultValue);
        Assert.AreEqual("confVoltDc", button.CommandId);
        CollectionAssert.AreEqual(new[] { "confVoltDc.Range" }, button.ParameterFieldIds);
        Assert.IsFalse(section.Controls.OfType<IndicatorControl>().Any(c => c.Id == "confVoltDc.reply"));
    }

    [TestMethod]
    public async Task InvokeAsync_MeasureVoltageDc_SendsTheExactScpiQuery()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, Profile, tracker: null);

        await surface.InvokeAsync("measVoltDc", null);

        VerifySent(transport, "MEAS:VOLT:DC?\n");
    }

    [TestMethod]
    public async Task InvokeAsync_ConfigureDcVoltageRange_SubstitutesTheChosenRange()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, Profile, tracker: null);

        await surface.InvokeAsync("confVoltDc", "10");

        VerifySent(transport, "CONF:VOLT:DC 10\n");
    }

    [TestMethod]
    [DataRow("+1.234560E-01\n", DisplayName = "LF-only, no leading query echo")]
    [DataRow("+1.234560E-01\r\n", DisplayName = "CRLF")]
    public void ScpiReplyPresenter_MeasureReply_UpdatesTheMatchingIndicatorWithTheRawValue(string wireReply)
    {
        // Realistic 34401A reply shape: signed scientific notation, no unit suffix, no command echo.
        var presenter = new ScpiReplyPresenter();
        IReadOnlyDictionary<string, string>? fired = null;
        presenter.ValuesChanged += (_, values) => fired = values;

        presenter.QuerySent("measVoltDc.reply");
        presenter.Render(new System.Buffers.ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(wireReply)));

        Assert.IsNotNull(fired);
        Assert.AreEqual("+1.234560E-01", fired!["measVoltDc.reply"]);
    }

    [TestMethod]
    public void ScpiReplyPresenter_SendCustomThenMeasure_CorrelatesBothRepliesInOrder()
    {
        // The realistic multi-button interaction: clicking a Measure button after using the Custom
        // Command field must not cross-wire the two replies to each other's indicators.
        var presenter = new ScpiReplyPresenter();
        var fired = new List<KeyValuePair<string, string>>();
        presenter.ValuesChanged += (_, values) => fired.AddRange(values);

        presenter.QuerySent("sendCustom.reply");
        presenter.QuerySent("measVoltDc.reply");
        presenter.Render(new System.Buffers.ReadOnlySequence<byte>(Encoding.ASCII.GetBytes("HP34401A,0,1.0\n+1.234560E-01\n")));

        Assert.HasCount(2, fired);
        Assert.AreEqual("sendCustom.reply", fired[0].Key);
        Assert.AreEqual("HP34401A,0,1.0", fired[0].Value);
        Assert.AreEqual("measVoltDc.reply", fired[1].Key);
        Assert.AreEqual("+1.234560E-01", fired[1].Value);
    }
}
