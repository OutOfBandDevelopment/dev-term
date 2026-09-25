using System.Text;
using System.Windows.Controls;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Devices.Scpi;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives the real <see cref="ControlPanelWindow"/> end to end against the real bundled 34401A
/// profile, a real <see cref="Session"/>, a real <see cref="ScpiControlSurface"/>, and a real
/// <see cref="ScpiReplyPresenter"/> wired into a real <see cref="Pipeline"/> — the same object graph
/// production code builds, driven the same way a user would (a real button click), with only the
/// transport swapped for the in-memory, pipe-backed <see cref="FakeTransport"/>. Written to actually
/// observe what the real Measure-button click → device-reply → indicator-update path does, rather
/// than asserting against each piece in isolation (see <c>ScpiReplyPresenterTests</c>,
/// <c>ScpiControlSurfaceTests</c>, <c>Hp34401aProfileTests</c> for those) — this is what would have
/// caught a wiring bug none of those per-class tests could see (e.g. the presenter never actually
/// subscribed, or the button never actually reaching <see cref="IControlSurface.InvokeAsync"/>).
/// Same <see cref="StaTestRunner"/>/no-<c>Show()</c> conventions as <see cref="MainWindowTests"/>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ScpiControlPanelEndToEndTests
{
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(5);

    private static ScpiInstrumentProfile Profile =>
        ScpiProfileCatalog.All.Single(p => p.Name.Contains("34401A", StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public void ClickingMeasureDcVoltage_ThenReceivingARealisticReply_UpdatesTheIndicatorOnScreen()
    {
        StaTestRunner.Run(async () =>
        {
            var transport = new FakeTransport();
            var presenter = new ScpiReplyPresenter();
            var session = new Session(transport, new Pipeline([presenter]));
            var surface = new ScpiControlSurface(session, Profile, presenter);
            var definition = ScpiUiDefinitionBuilder.Build(Profile);
            var window = new ControlPanelWindow(definition, surface, presenter) { ShowInTaskbar = false };
            await session.OpenAsync(TestContext.CancellationToken);
            StaTestRunner.DoEvents();

            // This is exactly what a user does: click the button. Nothing here reaches into
            // ScpiControlSurface or ScpiReplyPresenter directly.
            var button = (Button)window.ControlViews["measVoltDc"];
            button.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            StaTestRunner.DoEvents();

            // What the button click should have caused: the query sent over the wire.
            Assert.HasCount(1, transport.WrittenPayloads);
            Assert.AreEqual("MEAS:VOLT:DC?\n", Encoding.ASCII.GetString(transport.WrittenPayloads[0]));

            // Simulate the device answering, over the real Session pull loop — not a direct
            // ValuesChanged.Invoke shortcut.
            await transport.PushIncomingAsync(Encoding.ASCII.GetBytes("+1.234560E-01\r\n"));

            var updated = StaTestRunner.PumpUntil(() => window.IndicatorLabels["measVoltDc.reply"].Text == "+1.234560E-01", _pumpTimeout);

            Assert.IsTrue(updated, $"Expected the indicator to show the decoded reply; actual text was '{window.IndicatorLabels["measVoltDc.reply"].Text}'.");

            await session.CloseAsync(TestContext.CancellationToken);
        });
    }

    /// <summary>
    /// Re-test of the 2026-09-23 report "pressing Enter in the Custom Command field throws, and so
    /// does clicking Send with a command typed": committing the field (Enter and LostFocus share
    /// the same commit) must not error, and Send must put exactly the typed command on the wire.
    /// </summary>
    [TestMethod]
    public void CustomCommand_CommittingTheFieldThenClickingSend_SendsItWithNoError()
    {
        StaTestRunner.Run(async () =>
        {
            var transport = new FakeTransport();
            var presenter = new ScpiReplyPresenter();
            var session = new Session(transport, new Pipeline([presenter]));
            var surface = new ScpiControlSurface(session, Profile, presenter);
            var window = new ControlPanelWindow(ScpiUiDefinitionBuilder.Build(Profile), surface, presenter) { ShowInTaskbar = false };
            await session.OpenAsync(TestContext.CancellationToken);
            StaTestRunner.DoEvents();
            var statusBefore = window.StatusText.Text;

            var field = (TextBox)window.ControlViews[ScpiUiDefinitionBuilder.CustomCommandFieldId];
            field.Text = "*IDN?";
            field.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.UIElement.LostFocusEvent));
            StaTestRunner.DoEvents();

            Assert.IsEmpty(transport.WrittenPayloads, "Committing the field alone sends nothing.");

            var send = (Button)window.ControlViews[ScpiControlSurface.SendCustomCommandId];
            send.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            StaTestRunner.PumpUntil(() => transport.WrittenPayloads.Count > 0, _pumpTimeout);

            Assert.HasCount(1, transport.WrittenPayloads);
            Assert.AreEqual("*IDN?\n", Encoding.ASCII.GetString(transport.WrittenPayloads[0]));
            Assert.AreEqual(statusBefore, window.StatusText.Text, "No 'Command failed' error was shown.");

            await session.CloseAsync(TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public void CommandFails_ShowsTheErrorInThePanelInsteadOfCrashing()
    {
        StaTestRunner.Run(async () =>
        {
            var transport = new FakeTransport();
            var presenter = new ScpiReplyPresenter();
            var session = new Session(transport, new Pipeline([presenter]));
            var window = new ControlPanelWindow(ScpiUiDefinitionBuilder.Build(Profile), new ScpiControlSurface(session, Profile, presenter), presenter) { ShowInTaskbar = false };
            await session.OpenAsync(TestContext.CancellationToken);
            StaTestRunner.DoEvents();

            transport.FailNextWrite(new System.IO.IOException("device unplugged"));
            ((Button)window.ControlViews["measVoltDc"]).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

            Assert.IsTrue(
                StaTestRunner.PumpUntil(() => window.StatusText.Text.Contains("device unplugged", StringComparison.Ordinal), _pumpTimeout),
                $"Expected the failure in the status line; actual: '{window.StatusText.Text}'.");
        });
    }

    [TestMethod]
    public void NoPresenterWiredAsTracker_ReplyStillArrivesButIndicatorNeverUpdates()
    {
        // Reproduces the exact shape of the original real-hardware report ("device beeps, no
        // measurement shown anywhere"): if ScpiControlSurface isn't given the same presenter
        // instance that's actually wired into the live session's Pipeline as its IScpiReplyTracker
        // (e.g. the "scpi" presenter checkbox wasn't checked for that connection), QuerySent never
        // runs, so even a real reply arriving over the wire has nothing to correlate it to — the
        // indicator is never touched, even though decoding/line-buffering itself works fine.
        StaTestRunner.Run(async () =>
        {
            var transport = new FakeTransport();
            var presenter = new ScpiReplyPresenter();
            var session = new Session(transport, new Pipeline([presenter]));
            var surface = new ScpiControlSurface(session, Profile, tracker: null); // <- the bug shape
            var definition = ScpiUiDefinitionBuilder.Build(Profile);
            var window = new ControlPanelWindow(definition, surface, presenter) { ShowInTaskbar = false };
            await session.OpenAsync(TestContext.CancellationToken);
            StaTestRunner.DoEvents();

            var button = (Button)window.ControlViews["measVoltDc"];
            button.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            StaTestRunner.DoEvents();

            await transport.PushIncomingAsync(Encoding.ASCII.GetBytes("+1.234560E-01\r\n"));

            // No success condition to pump toward — just give the real pull loop a real window of
            // time to have processed the bytes, if it were going to update anything.
            StaTestRunner.PumpUntil(() => false, TimeSpan.FromMilliseconds(300));

            Assert.AreEqual(string.Empty, window.IndicatorLabels["measVoltDc.reply"].Text);

            await session.CloseAsync(TestContext.CancellationToken);
        });
    }

    public required TestContext TestContext { get; set; }
}
