using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Renders a real, laid-out WPF <see cref="Window"/> to a PNG file via
/// <see cref="RenderTargetBitmap"/> — the same "real captured output from the actual built app"
/// approach <c>docs/user-guide/README.md</c> already documents for the TUI (a real Terminal.Gui
/// screen buffer via <c>TuiTestRunner.DumpBuffer</c>), just against WPF's own render target instead
/// of a Terminal.Gui screen buffer. Never calls <see cref="Window.Show"/> — <see cref="RenderTargetBitmap"/>
/// renders the visual tree directly and needs no real HWND/display; <see cref="UIElement.Measure"/>/
/// <see cref="UIElement.Arrange"/>/<see cref="UIElement.UpdateLayout"/> is enough to force templates
/// to apply and real layout to run, confirmed empirically by inspecting the resulting PNG's pixel
/// content rather than assuming it.
/// </summary>
internal static class WpfScreenshot
{
    /// <summary>
    /// Shows <paramref name="window"/> off-screen (moved out past any real monitor, no taskbar
    /// entry). A <see cref="RenderTargetBitmap"/> render of a <see cref="Window"/> that was only
    /// ever <c>Measure</c>d/<c>Arrange</c>d but never shown comes back completely blank — confirmed
    /// empirically by inspecting the resulting PNG's actual pixels, not assumed — because it has no
    /// <see cref="System.Windows.Interop.HwndSource"/>/compositor target until a real (if invisible)
    /// <c>Show()</c> gives it one. This is also why <see cref="MainWindow"/> screenshots must drive
    /// its real <c>Loaded</c>-triggered auto-connect rather than calling <c>ConnectAsync</c>
    /// directly — see <c>CLAUDE.md</c>'s "Don't both call <c>Show()</c> and <c>ConnectAsync()</c>"
    /// constraint.
    ///
    /// Deliberately never closes the window afterward (same as <c>MainWindowTests</c>, which never
    /// calls <c>Close()</c> either): <see cref="MainWindow.OnClosing"/>'s cancel-then-async-cleanup-
    /// then-reclose pattern raced against this harness's single manually-pumped
    /// <see cref="System.Windows.Threading.DispatcherFrame"/> and threw
    /// "Cannot ... Close ... while a Window is closing" — a real reentrancy edge case worth its own
    /// investigation, but not one screenshot capture needs to resolve; the process exits shortly
    /// after these tests run regardless.
    /// </summary>
    public static void ShowOffScreen(Window window, double width = 900, double height = 650)
    {
        window.Width = width;
        window.Height = height;
        window.WindowStyle = WindowStyle.None;
        window.ShowInTaskbar = false;
        window.Left = -10000;
        window.Top = -10000;

        window.Show();
        window.UpdateLayout();
    }

    public static void Save(Window window, string path)
    {
        var width = (int)Math.Max(1, window.ActualWidth);
        var height = (int)Math.Max(1, window.ActualHeight);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
