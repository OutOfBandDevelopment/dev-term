using System.IO;
using System.Windows;
using DevTerm.Configuration;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// Drives a real <see cref="DeviceProfilesWindow"/> — real XAML, real controls, real
/// <c>{Binding ...}</c>/<c>Command="{Binding ...}"</c> wiring, no code-behind business logic to
/// test around. Most of the actual logic (validate/connect/load/save/import/export) already has
/// direct, front-end-agnostic coverage in <c>DevTerm.Configuration.Tests.ConnectionEditorViewModelTests</c>
/// — what's specific to test here is that the XAML bindings are wired correctly at all: a typo in a
/// <c>Binding</c> path fails silently at runtime (a debug-output warning, not an exception), so
/// this checks real control state reflects the view model and vice versa, not just that the view
/// model's own logic works in isolation.
///
/// Never calls <see cref="Window.Show"/>, same reasoning as <c>MainWindowTests</c> — not relevant
/// here (this window doesn't auto-connect on <c>Loaded</c>), but consistent with the rest of this
/// assembly.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class DeviceProfilesWindowTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-profiles-window-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    [TestMethod]
    public void Constructor_BindsInitialOptionsIntoRealControls()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", TcpPort = 23 };
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), initial) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                Assert.AreEqual("tcp", window.TransportBox.Text);
                Assert.AreEqual("192.168.0.107", window.HostBox.Text);
                Assert.AreEqual("23", window.TcpPortBox.Text);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void EditingATextBox_UpdatesTheViewModel_ThroughTheRealBinding()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.TransportBox.Text = "hid";

                Assert.AreEqual("hid", window.ViewModel.Transport);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ChangingTheViewModel_UpdatesTheRealTextBox_ThroughTheRealBinding()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                window.ViewModel.Host = "192.168.0.108";

                Assert.AreEqual("192.168.0.108", window.HostBox.Text);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectingATransport_TogglesWhichFieldGroupIsVisible()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions { Transport = "serial" }) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();

                Assert.AreEqual(Visibility.Visible, window.SerialPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.TcpPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.HidPanel.Visibility);

                window.ViewModel.Transport = "tcp";

                Assert.AreEqual(Visibility.Collapsed, window.SerialPanel.Visibility);
                Assert.AreEqual(Visibility.Visible, window.TcpPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.HidPanel.Visibility);

                window.ViewModel.Transport = "hid";

                Assert.AreEqual(Visibility.Collapsed, window.SerialPanel.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.TcpPanel.Visibility);
                Assert.AreEqual(Visibility.Visible, window.HidPanel.Visibility);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConnectButton_Command_SetsResultAndClosesTheDialog()
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var window = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions()) { ShowInTaskbar = false };
                StaTestRunner.DoEvents();
                window.TransportBox.Text = "tcp";
                window.HostBox.Text = "192.168.0.107";
                window.TcpPortBox.Text = "23";

                var closed = false;
                window.ViewModel.CloseRequested += (_, _) => closed = true;

                window.ViewModel.ConnectCommand.Execute(null);

                Assert.IsTrue(closed);
                Assert.IsNotNull(window.Result);
                Assert.AreEqual("tcp", window.Result.Transport);

                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
