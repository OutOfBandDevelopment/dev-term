using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives the real File &gt; New Session.../Close Session menu items (multi-session-ui Step 3) -
/// same "drive the actual production <c>Action</c>, not a copy of its logic" approach as
/// <see cref="TuiModeTests.K8055MenuItem_AfterThePanelCloses_UnsubscribesFromValuesChanged"/>. New
/// Session opens a real, nested <c>ConfigureMode</c> window via <c>app.Run</c>, so these need
/// <see cref="TuiTestRunner.RunWithLoop(Session, IPresenter, CliOptions, Action{TuiWindowParts}, ConnectionProfileStore)"/>
/// (a real loop pumping on a background thread, see <see cref="TuiTestRunner"/>'s doc comment) plus
/// an <c>AddTimeout</c> registered just ahead of invoking the menu item's <c>Action</c>, which only
/// fires once the nested dialog's own loop is pumping - the same technique
/// <see cref="TuiReview.Modal"/> and the K8055 test above use. The initial tab's transport is
/// "loopback" so the nested dialog's default fields are already a valid connection and only the
/// Connect button (found by walking <see cref="View.SubViews"/>, mirroring
/// <c>ConfigureModeTests.Click</c>'s <c>Command.Accept</c> invocation) needs pressing - no field
/// entry required.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class TuiModeMultiSessionTests
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    private static (Session Session, FakeTransport Transport, IPresenter Presenter) CreateLoopbackSession()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        return (session, transport, presenter);
    }

    /// <summary>Walks <paramref name="root"/>'s subview tree for a <see cref="Button"/> whose <see cref="View.Text"/> matches, the same way a person would find it by eye in the rendered dialog.</summary>
    private static Button FindButton(View root, string text)
    {
        foreach (var sub in root.SubViews)
        {
            if (sub is Button button && button.Text == text)
            {
                return button;
            }

            if (TryFindButton(sub, text, out var nested))
            {
                return nested;
            }
        }

        throw new InvalidOperationException($"No button titled '{text}' found under {root}.");
    }

    private static bool TryFindButton(View root, string text, out Button found)
    {
        try
        {
            found = FindButton(root, text);
            return true;
        }
        catch (InvalidOperationException)
        {
            found = null!;
            return false;
        }
    }

    /// <summary>Opens New Session's nested <c>ConfigureMode</c> dialog and immediately accepts its (already-valid, loopback) defaults via the Connect button - same <c>AddTimeout</c>-ahead-of-the-menu-action technique as <see cref="TuiModeTests.K8055MenuItem_AfterThePanelCloses_UnsubscribesFromValuesChanged"/>.</summary>
    private static void OpenNewSessionWithDefaults(IApplication app, TuiWindowParts parts) =>
        TuiTestRunner.InvokeOnLoop(() =>
        {
            app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
            {
                if (app.TopRunnableView is not { } dialog || dialog == parts.Window)
                {
                    return true;
                }

                FindButton(dialog, "Connect").InvokeCommand(Command.Accept);
                return false;
            });

            parts.NewSessionMenuItem.Action!.Invoke();
            return true;
        });

    [TestMethod]
    public async Task NewSessionMenuItem_WithAValidChoice_AddsATabAndConnectsIt()
    {
        var (session, _, presenter) = CreateLoopbackSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "loopback", Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            var app = TuiTestRunner.CurrentApp;

            Assert.AreEqual(1, TuiTestRunner.InvokeOnLoop(() => parts.AllSessions().Count), "Only the startup tab exists yet.");
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.CloseSessionMenuItem.Enabled), "Close Session is enabled even with only one tab, so the window can reach zero tabs (Step 4).");

            OpenNewSessionWithDefaults(app, parts);

            var addedTab = TuiTestRunner.WaitUntilOnLoop(() => parts.AllSessions().Count == 2, _waitTimeout);
            Assert.IsTrue(addedTab, "Expected a second session tab after New Session.");
            Assert.AreEqual(2, TuiTestRunner.InvokeOnLoop(() => parts.TabsView.TabCollection.Count()));
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.CloseSessionMenuItem.Enabled), "Close Session should become enabled once a second tab exists.");

            var newTabIsActive = TuiTestRunner.InvokeOnLoop(() => parts.TabsView.Value != parts.Output);
            Assert.IsTrue(newTabIsActive, "Expected New Session to switch the active tab to the one it just opened.");

            var newSessionConnected = TuiTestRunner.WaitUntilOnLoop(
                () => parts.AllSessions().Any(s => !ReferenceEquals(s, session) && s.State == ConnectionState.Open),
                _waitTimeout);
            Assert.IsTrue(newSessionConnected, "Expected the new tab's own session to have connected.");
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task CloseSessionMenuItem_WithMultipleTabs_ClosesTheActiveTabAndSwitchesToAnother()
    {
        var (session, _, presenter) = CreateLoopbackSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "loopback", Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            var app = TuiTestRunner.CurrentApp;

            OpenNewSessionWithDefaults(app, parts);
            var addedTab = TuiTestRunner.WaitUntilOnLoop(() => parts.AllSessions().Count == 2, _waitTimeout);
            Assert.IsTrue(addedTab, "Expected a second session tab before testing Close Session.");

            TuiTestRunner.InvokeOnLoop(() =>
            {
                parts.CloseSessionMenuItem.Action!.Invoke();
                return true;
            });

            var closedBackToOne = TuiTestRunner.WaitUntilOnLoop(() => parts.AllSessions().Count == 1, _waitTimeout);
            Assert.IsTrue(closedBackToOne, "Expected Close Session to remove the active (new) tab.");
            Assert.AreEqual(1, TuiTestRunner.InvokeOnLoop(() => parts.TabsView.TabCollection.Count()));
            Assert.IsTrue(TuiTestRunner.InvokeOnLoop(() => parts.CloseSessionMenuItem.Enabled), "Close Session stays enabled with one tab remaining, so the window can reach zero tabs (Step 4).");
            Assert.AreSame(session, TuiTestRunner.InvokeOnLoop(() => parts.AllSessions()[0]), "The original (startup) tab's session should be the one left open, not the closed one.");
            Assert.AreEqual(ConnectionState.Open, session.State, "Closing the other tab must not touch the remaining tab's own session.");
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task CloseSessionMenuItem_WithOnlyOneTab_ReachesTheZeroTabState()
    {
        var (session, _, presenter) = CreateLoopbackSession();
        await session.OpenAsync(TestContext.CancellationToken);
        var cliOptions = new CliOptions { Transport = "loopback", Presenter = ["ascii"] };

        TuiTestRunner.RunWithLoop(session, presenter, cliOptions, parts =>
        {
            TuiTestRunner.InvokeOnLoop(() =>
            {
                parts.CloseSessionMenuItem.Action!.Invoke();
                return true;
            });

            var reachedZeroTabs = TuiTestRunner.WaitUntilOnLoop(() => parts.AllSessions().Count == 0, _waitTimeout);
            Assert.IsTrue(reachedZeroTabs, "Closing the only tab should leave the window open with zero tabs (Step 4), not be a no-op.");
            Assert.AreEqual(0, TuiTestRunner.InvokeOnLoop(() => parts.TabsView.TabCollection.Count()));
            Assert.IsFalse(TuiTestRunner.InvokeOnLoop(() => parts.CloseSessionMenuItem.Enabled), "Close Session should disable itself once there are no tabs left to close.");
            Assert.IsFalse(TuiTestRunner.InvokeOnLoop(() => parts.SendField.Enabled), "The send field should be disabled with no active tab.");
            Assert.AreEqual(string.Empty, TuiTestRunner.InvokeOnLoop(() => parts.SendField.Text.ToString()), "The send field should be cleared with no active tab.");
            Assert.AreEqual("dev-term", TuiTestRunner.InvokeOnLoop(() => parts.Window.Title.ToString()), "The window title should reset once there are no tabs left.");
            Assert.IsTrue(
                TuiTestRunner.InvokeOnLoop(() => parts.StatusLabel.Text.ToString()).Contains("No sessions open", StringComparison.Ordinal),
                "The status line should say no sessions are open.");
        });

        await session.CloseAsync(TestContext.CancellationToken);
    }

    public required TestContext TestContext { get; set; }
}
