using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Devices.Scpi.Tests;

/// <summary>
/// Verifies the exact bytes <see cref="ScpiControlSurface"/> sends for a command — template
/// substitution, terminator handling, custom-command passthrough — against a mocked
/// <see cref="ITransport"/> behind a real <see cref="Session"/>, and its query/tracker
/// correlation via a mocked <see cref="IScpiReplyTracker"/>. No real instrument involved, so UNIT.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ScpiControlSurfaceTests
{
    private static (Session Session, Mock<ITransport> Transport) CreateSurfaceSession()
    {
        var transport = new Mock<ITransport>();
        var session = new Session(transport.Object, new Pipeline([]));
        return (session, transport);
    }

    private static ScpiInstrumentProfile BuildProfile(string terminator = "\n") => new()
    {
        Name = "Test Instrument",
        Terminator = terminator,
        Commands =
        [
            new ScpiCommandDefinition { Id = "idn", Label = "Identify", Template = "*IDN?", IsQuery = true },
            new ScpiCommandDefinition { Id = "rst", Label = "Reset", Template = "*RST" },
            new ScpiCommandDefinition
            {
                Id = "freq",
                Label = "Set Frequency",
                Template = "SOUR1:FREQ {Frequency}",
                Parameters = [new ScpiParameterDefinition { Name = "Frequency", Kind = ScpiParameterKind.Numeric, Minimum = 0, Maximum = 1000, DefaultValue = "10" }],
            },
            new ScpiCommandDefinition
            {
                Id = "conf",
                Label = "Configure",
                Template = "CONF:{Function} {Range}",
                Parameters =
                [
                    new ScpiParameterDefinition { Name = "Function", Kind = ScpiParameterKind.Text, DefaultValue = "VOLT:DC" },
                    new ScpiParameterDefinition { Name = "Range", Kind = ScpiParameterKind.Choice, Options = ["AUTO", "10"], DefaultValue = "AUTO" },
                ],
            },
        ],
    };

    private static void VerifySent(Mock<ITransport> transport, string expectedText)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expectedText);
        transport.Verify(t => t.WriteAsync(
            It.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(expectedBytes)),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task InvokeAsync_ZeroParameterCommand_SendsTemplateVerbatimWithTerminator()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await surface.InvokeAsync("rst", null, TestContext.CancellationToken);

        VerifySent(transport, "*RST\n");
    }

    [TestMethod]
    public async Task InvokeAsync_SingleParameterCommand_SubstitutesToken()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await surface.InvokeAsync("freq", "123.5", TestContext.CancellationToken);

        VerifySent(transport, "SOUR1:FREQ 123.5\n");
    }

    [TestMethod]
    public async Task InvokeAsync_MultiParameterCommand_SubstitutesEachTokenPositionally()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await surface.InvokeAsync("conf", "VOLT:AC,10", TestContext.CancellationToken);

        VerifySent(transport, "CONF:VOLT:AC 10\n");
    }

    [TestMethod]
    public async Task InvokeAsync_NumericParameterOutOfRange_ClampsToBounds()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await surface.InvokeAsync("freq", "5000", TestContext.CancellationToken);

        VerifySent(transport, "SOUR1:FREQ 1000\n");
    }

    [TestMethod]
    public async Task InvokeAsync_MissingParameterValue_FallsBackToDefault()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await surface.InvokeAsync("freq", null, TestContext.CancellationToken);

        VerifySent(transport, "SOUR1:FREQ 10\n");
    }

    [TestMethod]
    public async Task InvokeAsync_CustomTerminator_IsAppendedInsteadOfNewline()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(terminator: string.Empty), tracker: null);

        await surface.InvokeAsync("rst", null, TestContext.CancellationToken);

        VerifySent(transport, "*RST");
    }

    [TestMethod]
    public async Task InvokeAsync_SendCustomCommand_SendsValueVerbatimWithNoTemplate()
    {
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await surface.InvokeAsync(ScpiControlSurface.SendCustomCommandId, "MEAS:VOLT:DC?", TestContext.CancellationToken);

        VerifySent(transport, "MEAS:VOLT:DC?\n");
    }

    [TestMethod]
    public async Task InvokeAsync_QueryCommand_RegistersReplyIndicatorWithTrackerBeforeSending()
    {
        var (session, _) = CreateSurfaceSession();
        var tracker = new Mock<IScpiReplyTracker>();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker.Object);

        await surface.InvokeAsync("idn", null, TestContext.CancellationToken);

        tracker.Verify(t => t.QuerySent("idn.reply"), Times.Once);
    }

    [TestMethod]
    public async Task InvokeAsync_NonQueryCommand_NeverRegistersWithTracker()
    {
        var (session, _) = CreateSurfaceSession();
        var tracker = new Mock<IScpiReplyTracker>();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker.Object);

        await surface.InvokeAsync("rst", null, TestContext.CancellationToken);

        tracker.Verify(t => t.QuerySent(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task InvokeAsync_SendCustomCommand_RegistersItsOwnReplyIndicator()
    {
        var (session, _) = CreateSurfaceSession();
        var tracker = new Mock<IScpiReplyTracker>();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker.Object);

        await surface.InvokeAsync(ScpiControlSurface.SendCustomCommandId, "*IDN?", TestContext.CancellationToken);

        tracker.Verify(t => t.QuerySent($"{ScpiControlSurface.SendCustomCommandId}.reply"), Times.Once);
    }

    [TestMethod]
    public async Task InvokeAsync_NumericParameterWithDecimalPlaces_PadsToFixedWidth()
    {
        // Real-hardware-confirmed: a Korad KA3005P/KA6003P has no command terminator and parses a
        // fixed-width field after "VSET1:" — sending "12" instead of "12.00" desyncs its parser and
        // the set silently fails. Confirmed on real hardware that even "5.00" (no leading zero) is
        // one character short of the expected "05.00" width and the set still silently fails (a
        // follow-up VSET1? query read back the unchanged previous value) — both DecimalPlaces and
        // IntegerDigits are required. See ScpiParameterDefinition.DecimalPlaces/IntegerDigits.
        var (session, transport) = CreateSurfaceSession();
        var profile = new ScpiInstrumentProfile
        {
            Name = "Test Instrument",
            Terminator = string.Empty,
            Commands =
            [
                new ScpiCommandDefinition
                {
                    Id = "vset",
                    Label = "Set Voltage",
                    Template = "VSET1:{Voltage}",
                    Parameters = [new ScpiParameterDefinition { Name = "Voltage", Kind = ScpiParameterKind.Numeric, Minimum = 0, Maximum = 30, DecimalPlaces = 2, IntegerDigits = 2 }],
                },
            ],
        };
        var surface = new ScpiControlSurface(session, profile, tracker: null);

        await surface.InvokeAsync("vset", "5", TestContext.CancellationToken);

        VerifySent(transport, "VSET1:05.00");
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownCommand_Throws()
    {
        var (session, _) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => surface.InvokeAsync("notARealCommand", null, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task InvokeAsync_ParameterFieldId_IsANoOpRatherThanThrowing()
    {
        // Real-hardware-confirmed on the KA6003P's WPF control panel: ScpiUiDefinitionBuilder gives
        // a multi-parameter command's own field an id of "{command.Id}.{parameter.Name}" (e.g.
        // "vset.Voltage") so it's unique across the panel, but it's a value holder read by the
        // command's own button, not a command in its own right. The generic
        // ControlPanelMode/ControlPanelWindow renderers commit every field on blur/Enter by calling
        // InvokeAsync with the field's own id like any other field — that must not throw here, or
        // simply tabbing off the field (with no button click at all) crashes the app.
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await surface.InvokeAsync("freq.Frequency", "42", TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task InvokeAsync_CustomCommandFieldId_IsANoOpRatherThanThrowing()
    {
        // Same bug shape as above, for the always-present "Custom Command" text field (id
        // "customCommand") that sendCustom's ParameterFieldIds reads from.
        var (session, transport) = CreateSurfaceSession();
        var surface = new ScpiControlSurface(session, BuildProfile(), tracker: null);

        await surface.InvokeAsync(ScpiUiDefinitionBuilder.CustomCommandFieldId, "*IDN?", TestContext.CancellationToken);

        transport.Verify(t => t.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public required TestContext TestContext { get; set; }
}
