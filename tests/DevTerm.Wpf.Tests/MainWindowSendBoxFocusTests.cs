using System.Windows.Input;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Regression coverage for bug 062: clicking "Send" with the mouse (unlike pressing Enter in
/// <c>SendBox</c>) moves WPF keyboard focus to that button - standard framework behavior for any
/// focusable control. With nothing restoring focus afterward, Up/Down stopped recalling history
/// until the user clicked back into the box. Needs a really-shown window (<see cref="WpfScreenshot.ShowOffScreen"/>,
/// not the <c>ConnectAsync</c>-direct-call convention most of <c>MainWindowTests</c> uses) because
/// <c>IsKeyboardFocused</c>/<c>IsKeyboardFocusWithin</c> only reflect reality once there's a real
/// <c>PresentationSource</c> - see <see cref="UiLayoutReviewTests"/>'s identical reasoning for its
/// own focus-visual checks. Asserts <c>IsKeyboardFocusWithin</c>, not <c>IsKeyboardFocused</c>: an
/// editable <see cref="System.Windows.Controls.ComboBox"/> delegates actual keyboard focus to its
/// internal text-box part, so <c>SendBox.IsKeyboardFocused</c> is <c>false</c> even right after
/// <c>SendBox.Focus()</c> succeeds - confirmed empirically (both tests failed on that property
/// before being switched to <c>IsKeyboardFocusWithin</c>, despite the production fix being correct).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class MainWindowSendBoxFocusTests
{
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(5);

    private static (MainWindow Window, FakeTransport Transport) CreateMainWindow()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        var window = new MainWindow(session, new PresenterCatalog([presenter]), new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Parser = "ascii" }, IsolatedProfiles.Empty())
        {
            ShowInTaskbar = false,
        };
        return (window, transport);
    }

    [TestMethod]
    public void SendCurrentInputAsync_RestoresFocusToSendBox_SoArrowKeysKeepRecallingHistory()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateMainWindow();
            WpfScreenshot.ShowOffScreen(window);
            StaTestRunner.PumpUntil(() => window.SendBox.IsEnabled, _pumpTimeout);

            window.SendBox.Text = "ID?";

            // Simulates what a real mouse click on the "Send" button does: a focusable control
            // takes keyboard focus when clicked, moving it away from SendBox.
            Keyboard.Focus(window.ParserBox);
            Assert.IsFalse(window.SendBox.IsKeyboardFocusWithin, "Precondition: focus moved away from SendBox, as it would after clicking Send.");

            await window.SendCurrentInputAsync();

            Assert.IsTrue(window.SendBox.IsKeyboardFocusWithin, "SendCurrentInputAsync should return focus to SendBox so Up/Down keeps recalling history right after a mouse-driven send.");
        });
    }

    [TestMethod]
    public void SendCurrentInputAsync_WithEmptyInput_StillRestoresFocusToSendBox()
    {
        StaTestRunner.Run(async () =>
        {
            var (window, _) = CreateMainWindow();
            WpfScreenshot.ShowOffScreen(window);
            StaTestRunner.PumpUntil(() => window.SendBox.IsEnabled, _pumpTimeout);

            window.SendBox.Text = string.Empty;
            Keyboard.Focus(window.ParserBox);

            await window.SendCurrentInputAsync();

            Assert.IsTrue(window.SendBox.IsKeyboardFocusWithin, "Clicking Send on an empty box is still a click, so focus should still return to SendBox.");
        });
    }
}
