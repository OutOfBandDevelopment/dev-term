using System.Windows;
using System.Windows.Controls;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The custom-color picker's OK/Cancel buttons must always be reachable. The window used to be a
/// fixed 360px tall and not resizable, with the button row docked last, so its content overflowed
/// and OK was cut off below the bottom edge (reported on the Busylight panel's Custom... button).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ColorPickerWindowTests
{
    // Shown for real (layout needs it), but off-screen and never closed - see WpfScreenshot.
    private static ColorPickerWindow ShowOffScreen()
    {
        var window = new ColorPickerWindow(10, 20, 30)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    // Whether the whole button sits inside the window's client area.
    private static bool IsFullyVisible(Window window, Button button)
    {
        var content = (FrameworkElement)window.Content;
        var bottomRight = button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), content);
        return button.IsVisible
            && bottomRight.Y <= content.ActualHeight + 0.5
            && bottomRight.X <= content.ActualWidth + 0.5;
    }

    [TestMethod]
    public void AtItsNaturalSize_OkAndCancelAreFullyOnScreen()
    {
        StaTestRunner.Run(async () =>
        {
            var window = ShowOffScreen();

            Assert.IsTrue(IsFullyVisible(window, window.OkButton), "OK is cut off.");
            Assert.IsTrue(IsFullyVisible(window, window.CancelButton), "Cancel is cut off.");
            Assert.AreNotEqual(ResizeMode.NoResize, window.ResizeMode, "The window must be resizable.");

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ShrunkBelowItsContent_ButtonsStayVisible_AndTheControlsScroll()
    {
        StaTestRunner.Run(async () =>
        {
            var window = ShowOffScreen();

            window.SizeToContent = SizeToContent.Manual;
            window.Height = window.MinHeight;
            window.UpdateLayout();

            Assert.IsTrue(IsFullyVisible(window, window.OkButton), "OK must stay pinned and visible.");
            Assert.IsGreaterThan(0, window.ControlsScroller.ScrollableHeight, "The controls should scroll instead of being clipped.");

            await Task.CompletedTask;
        });
    }
}
