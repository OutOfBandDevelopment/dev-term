using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DevTerm.Configuration;
using DevTerm.Core;
using DevTerm.Core.Plugins;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives the WPF plugin approval prompts for real: the Device > Plugin approvals... item raises the actual modal
/// <c>MessageBox</c> (the run-this-plugin question, then the per-approval picker). A helper thread finds each
/// dialog by window class, saves it with <c>PrintWindow</c> for the user guide, and presses a button with
/// <c>BM_CLICK</c> (no real keyboard or mouse, so foreground-lock rules don't matter). The effect on
/// <see cref="LivePlugins"/> and <see cref="PluginTrust"/> is asserted afterwards.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class PluginApprovalDialogTests
{
    private const int _idYes = 6;
    private const uint _bmClick = 0x00F5;

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static string ImagesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "docs", "user-guide", "images");
    }

    /// <summary>The visible standard dialog ("#32770") of this process, if one is up.</summary>
    private static IntPtr FindDialog(out string title)
    {
        var pid = (uint)Environment.ProcessId;
        var found = IntPtr.Zero;
        var foundTitle = string.Empty;
        EnumWindows(
            (hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var owner);
                if (owner != pid || !IsWindowVisible(hwnd))
                {
                    return true;
                }

                var cls = new StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() != "#32770")
                {
                    return true;
                }

                var text = new StringBuilder(256);
                GetWindowText(hwnd, text, text.Capacity);
                found = hwnd;
                foundTitle = text.ToString();
                return false;
            },
            IntPtr.Zero);
        title = foundTitle;
        return found;
    }

    private static IntPtr FindButton(IntPtr dialog, int id)
    {
        var found = IntPtr.Zero;
        EnumChildWindows(
            dialog,
            (child, _) =>
            {
                if (GetDlgCtrlID(child) == id)
                {
                    found = child;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);
        return found;
    }

    private static void Capture(IntPtr hwnd, string path)
    {
        GetWindowRect(hwnd, out var rect);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            PrintWindow(hwnd, memory, 2); // PW_RENDERFULLCONTENT
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [TestMethod]
    public void ApprovalPrompt_ThenPicker_AreCapturedAndTheirChoicesTakeEffect()
    {
        var home = Path.Combine(Path.GetTempPath(), "devterm-wpf-plugins-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(home, "plugins", "shout");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "shout.py"), "print('x')");
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "name": "py-shout", "version": "1.2.0", "contract": 1, "process": { "command": "python", "arguments": ["{folder}/shout.py"] } }""");
        var previous = Environment.GetEnvironmentVariable(DevTermHome.EnvironmentVariable);
        Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, home);
        PluginTrust.Approver = null;
        LivePlugins.Reset();
        PluginLoader.LoadAll(Path.Combine(home, "plugins"), new ServiceCollection());
        Assert.AreEqual(1, LivePlugins.Pending.Count, "The plugin should be waiting for approval.");

        var images = ImagesDirectory();
        Directory.CreateDirectory(images);
        var steps = new[]
        {
            (Title: "Run plugin program?", Image: "wpf-plugin-approval.png"),
            (Title: "dev-term — plugin approvals", Image: "wpf-plugin-approvals.png"),
        };
        string? failure = null;
        using var finished = new CancellationTokenSource();

        // Off the UI thread, because the UI thread is inside each modal MessageBox's own loop.
        var helper = Task.Run(() =>
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                foreach (var (title, image) in steps)
                {
                    IntPtr dialog;
                    string actual;
                    while ((dialog = FindDialog(out actual)) == IntPtr.Zero || actual != title)
                    {
                        if (DateTime.UtcNow > deadline || finished.IsCancellationRequested)
                        {
                            throw new TimeoutException($"No '{title}' dialog appeared (saw '{actual}').");
                        }

                        Thread.Sleep(50);
                    }

                    Thread.Sleep(300); // let it paint
                    Capture(dialog, Path.Combine(images, image));
                    var button = FindButton(dialog, _idYes);
                    if (button == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"'{title}' has no Yes button.");
                    }

                    SendMessage(button, _bmClick, IntPtr.Zero, IntPtr.Zero);
                    Thread.Sleep(200);
                }
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                // Unblock the UI thread: dismiss whatever dialog is stuck open.
                for (var i = 0; i < 4 && FindDialog(out _) is var d && d != IntPtr.Zero; i++)
                {
                    SendMessage(d, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                    Thread.Sleep(100);
                }
            }
        });

        try
        {
            StaTestRunner.Run(async () =>
            {
                var presenter = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
                var session = new Session(new FakeTransport(), new Pipeline([presenter]));
                var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Parser = presenter.Name };
                var window = new MainWindow(session, new PresenterCatalog([presenter]), options, IsolatedProfiles.Empty())
                {
                    ShowInTaskbar = false,
                };

                // Device > Plugin approvals...: asks about the waiting plugin, then walks the remembered approvals.
                window.PluginApprovalsMenuItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                await Task.CompletedTask;
            });
        }
        finally
        {
            finished.Cancel();
            helper.Wait(TimeSpan.FromSeconds(10));
        }

        try
        {
            Assert.IsNull(failure, failure);
            Assert.IsTrue(File.Exists(Path.Combine(images, "wpf-plugin-approval.png")));
            Assert.IsTrue(File.Exists(Path.Combine(images, "wpf-plugin-approvals.png")));
            CollectionAssert.Contains(LivePlugins.ActiveNames.ToList(), "py-shout", "Yes (always) should have made the plugin live at once.");
            Assert.AreEqual(0, PluginTrust.Approvals().Count, "Yes in the picker should have forgotten the remembered approval.");
        }
        finally
        {
            PluginTrust.Approver = null;
            LivePlugins.Reset();
            Environment.SetEnvironmentVariable(DevTermHome.EnvironmentVariable, previous);
            Directory.Delete(home, true);
        }
    }
}
