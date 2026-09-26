using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.DeviceManifests;
using DevTerm.DeviceManifests.Editing;
using DevTerm.Devices.Busylight;
using DevTerm.Devices.K8055;
using DevTerm.Devices.Scpi;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// The layout regression suite: every WPF window, in the states worth looking at, at its default size,
/// its minimum size and a large size, in both built-in themes - each checked by
/// <see cref="WpfLayoutAssert"/> (nothing off-window, overlapping, clipped, too small to use, or
/// unreadable) and captured to <c>artifacts/ui-review/wpf/</c> for a person to look over. See
/// <see cref="UiReview"/>. Deliberate exceptions are <see cref="LayoutAllowance"/>s, each with its reason.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class UiLayoutReviewTests
{
    private static readonly TimeSpan _pumpTimeout = TimeSpan.FromSeconds(5);

    public required TestContext TestContext { get; set; }

    [TestCleanup]
    public void ResetTheme() => ActiveTheme.Reset();

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-ui-review", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static (MainWindow Window, FakeTransport Transport) CreateMainWindow()
    {
        var transport = new FakeTransport();
        var presenter = new AsciiPresenter(Options.Create(new AsciiPresenterOptions()));
        var session = new Session(transport, new Pipeline([presenter]));
        var window = new MainWindow(session, new PresenterCatalog([presenter]), new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Parser = "ascii" }, IsolatedProfiles.Empty());
        return (window, transport);
    }

    // ---------------------------------------------------------------- MainWindow

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void MainWindow_Connected(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var (window, transport) = CreateMainWindow();
            var sizes = ui.SizesFor(window);
            UiReview.Show(window, sizes[0]);
            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.SendBox.IsEnabled, _pumpTimeout));
            await transport.PushIncomingAsync("ID TEK/2230,V81.1,VERS:14\r"u8.ToArray());
            StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 1, _pumpTimeout);
            window.SendBox.Text = "*IDN?";
            window.OutputList.Items.Add(new OutputLine("[error] Could not send: the device did not answer.", OutputKind.Error));

            ui.Review(window, "main-connected", sizes: sizes);
            window.Close();
            ui.AssertClean();
        });
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void MainWindow_Disconnected(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var (window, _) = CreateMainWindow();
            var sizes = ui.SizesFor(window);
            UiReview.Show(window, sizes[0]);
            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.SendBox.IsEnabled, _pumpTimeout));
            await window.ToggleConnectionAsync();
            UiReview.Settle(window);
            Assert.IsFalse(window.SendBox.IsEnabled);

            ui.Review(window, "main-disconnected", sizes: sizes);
            window.Close();
            ui.AssertClean();
        });
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void MainWindow_LongReplyLines(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var (window, transport) = CreateMainWindow();
            var sizes = ui.SizesFor(window);
            UiReview.Show(window, sizes[0]);
            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.SendBox.IsEnabled, _pumpTimeout));
            var lines = string.Concat(Enumerable.Range(0, 60).Select(i => $"{i:000} " + string.Concat(Enumerable.Repeat("CH1 VOLTS:1,COUPLING:DC;", i % 7 == 0 ? 12 : 1)) + "\r"));
            await transport.PushIncomingAsync(Encoding.ASCII.GetBytes(lines));
            StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 60, _pumpTimeout);

            ui.Review(window, "main-long-lines", sizes: sizes);
            window.Close();
            ui.AssertClean();
        });
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void MainWindow_Logging_AndItsMenus(string theme)
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var ui = new UiReview(theme);
                var (window, transport) = CreateMainWindow();
                var sizes = ui.SizesFor(window);
                UiReview.Show(window, sizes[0]);
                Assert.IsTrue(StaTestRunner.PumpUntil(() => window.SendBox.IsEnabled, _pumpTimeout));
                Assert.IsTrue(window.StartLogging(Path.Combine(directory, "20260925-120000_tcp_192.168.0.107_23.jsonl")));
                await transport.PushIncomingAsync("ID TEK/2230,V81.1,VERS:14\r"u8.ToArray());
                StaTestRunner.PumpUntil(() => window.OutputList.Items.Count > 1, _pumpTimeout);

                ui.Review(window, "main-logging", sizes: sizes);

                window.Width = 900;
                window.Height = 600;
                UiReview.Settle(window);
                var menu = WpfLayoutAssert.Descendants(window).OfType<Menu>().Single();
                foreach (var item in menu.Items.OfType<MenuItem>())
                {
                    var header = ((string)item.Header).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                    ui.ReviewMenu(item, $"main-menu-{header}");
                    if (item.Items.OfType<MenuItem>().FirstOrDefault(i => i.Name == "ThemeMenuItem") is { } themeItem)
                    {
                        ui.ReviewMenu(themeItem, "main-menu-view-theme");
                        themeItem.IsSubmenuOpen = false;
                    }

                    item.IsSubmenuOpen = false;
                    UiReview.Settle(window);
                }

                window.StopLogging();
                window.Close();
                ui.AssertClean();
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---------------------------------------------------------------- DeviceProfilesWindow

    [TestMethod]
    [DataRow("light", "serial")]
    [DataRow("dark", "serial")]
    [DataRow("light", "tcp")]
    [DataRow("dark", "tcp")]
    [DataRow("light", "hid")]
    [DataRow("dark", "hid")]
    [DataRow("light", "usbtmc")]
    [DataRow("dark", "usbtmc")]
    [DataRow("light", "loopback")]
    [DataRow("dark", "loopback")]
    public void DeviceProfilesWindow_EachTransport(string theme, string transport)
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] });
            store.Save("korad-ka3005p-bench-supply", new CliOptions { Transport = "serial", Port = "COM4", Presenter = ["scpi"] });
            StaTestRunner.Run(async () =>
            {
                var ui = new UiReview(theme);
                var initial = transport switch
                {
                    "serial" => new CliOptions { Transport = "serial", Port = "COM3", Baud = 9600, Presenter = ["ascii", "hex"], Description = "Tektronix 2230 bench scope" },
                    "tcp" => new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii"] },
                    "hid" => new CliOptions { Transport = "hid", VendorId = 4216, ProductId = 63560, Presenter = ["hex"] },
                    "usbtmc" => new CliOptions { Transport = "usbtmc", VendorId = 0x1AB1, ProductId = 0x0588, Presenter = ["scpi"] },
                    _ => new CliOptions { Transport = "loopback", Presenter = ["ascii"] },
                };
                var window = new DeviceProfilesWindow(store, initial);
                ui.Review(window, $"profiles-{transport}");
                window.Close();
                ui.AssertClean();
                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void DeviceProfilesWindow_ValidationErrorAndNotFoundHints(string theme)
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var ui = new UiReview(theme);

                // A port no machine has (the "not found" hint) and a long startup connection error in
                // the status line (what App passes when the saved profile fails to connect).
                var serial = new DeviceProfilesWindow(
                    new ConnectionProfileStore(directory),
                    new CliOptions { Transport = "serial", Port = "COM_DEVTERM_NOPE" },
                    "Could not connect to serial://COM_DEVTERM_NOPE: the port does not exist or is in use by another program. Check the cable, or pick another port below.");
                StaTestRunner.DoEvents();
                Assert.AreEqual(Visibility.Visible, serial.PortNotFoundText.Visibility);
                ui.Review(serial, "profiles-serial-errors");
                serial.Close();

                // A field that fails validation on Connect.
                var tcp = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23" });
                tcp.ViewModel.Host = string.Empty;
                tcp.ViewModel.ConnectCommand.Execute(null);
                StaTestRunner.DoEvents();
                Assert.IsFalse(string.IsNullOrEmpty(tcp.ViewModel.StatusMessage));
                ui.Review(tcp, "profiles-tcp-validation-error");

                // The edit made it dirty: closing would otherwise ask (a real, blocking message box).
                tcp.ViewModel.ConfirmDiscardChanges = () => true;
                tcp.Close();

                var usb = new DeviceProfilesWindow(new ConnectionProfileStore(directory), new CliOptions { Transport = "usbtmc", VendorId = 0xFFFE, ProductId = 0xFFFE });
                StaTestRunner.DoEvents();
                Assert.AreEqual(Visibility.Visible, usb.UsbDeviceNotFoundText.Visibility);
                ui.Review(usb, "profiles-usbtmc-not-found");
                usb.Close();

                ui.AssertClean();
                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---------------------------------------------------------------- ControlPanelWindow

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void ControlPanel_K8055(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var transport = new FakeTransport();
            var decoder = new K8055Decoder();
            var session = new Session(transport, new Pipeline([decoder]));
            SectionExpansionState.Forget(K8055UiDefinition.Build().Name);
            var window = new ControlPanelWindow(K8055UiDefinition.Build(), new K8055ControlSurface(session), decoder);
            await session.OpenAsync(TestContext.CancellationToken);
            var sizes = ui.SizesFor(window);
            UiReview.Show(window, sizes[0]);
            await transport.PushIncomingAsync([0x00, 0x05, 0x03, 42, 80, 5, 0, 10, 0]);
            StaTestRunner.PumpUntil(() => window.IndicatorLabels["analogIn1"].Text == "42", _pumpTimeout);

            ui.Review(window, "panel-k8055", ControlPanelOptions(window), sizes);
            await session.CloseAsync(TestContext.CancellationToken);
            window.Close();
            ui.AssertClean();
        });
    }

    [TestMethod]
    [DataRow("light", false)]
    [DataRow("dark", false)]
    [DataRow("light", true)]
    [DataRow("dark", true)]
    public void ControlPanel_Busylight(string theme, bool customColor)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            if (customColor)
            {
                LastPickedColors.Set("customColor", (0xFF, 0x66, 0x00));
            }
            else
            {
                LastPickedColors.Forget("customColor");
            }

            try
            {
                SectionExpansionState.Forget(BusylightUiDefinition.Build().Name);
                var session = new Session(new FakeTransport(), new Pipeline([]));
                var window = new ControlPanelWindow(BusylightUiDefinition.Build(), new BusylightControlSurface(session), structuredSource: null);
                ui.Review(window, customColor ? "panel-busylight-custom-color" : "panel-busylight", ControlPanelOptions(window));
                window.Close();
            }
            finally
            {
                LastPickedColors.Forget("customColor");
            }

            ui.AssertClean();
            await Task.CompletedTask;
        });
    }

    public static IEnumerable<object[]> ScpiProfilesAndThemes =>
        new[] { ScpiProfileCatalog.Generic }.Concat(ScpiProfileCatalog.All)
            .SelectMany(p => new[] { new object[] { "light", p.Name }, ["dark", p.Name] });

    [TestMethod]
    [DynamicData(nameof(ScpiProfilesAndThemes))]
    public void ControlPanel_EveryScpiProfile(string theme, string profileName)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var profile = profileName == ScpiProfileCatalog.Generic.Name ? ScpiProfileCatalog.Generic : ScpiProfileCatalog.All.Single(p => p.Name == profileName);
            var presenter = new ScpiReplyPresenter();
            presenter.ConfigureTerminator(profile.Terminator);
            var session = new Session(new FakeTransport(), new Pipeline([presenter]));
            var definition = ScpiUiDefinitionBuilder.Build(profile);
            SectionExpansionState.Forget(definition.Name);
            var window = new ControlPanelWindow(definition, new ScpiControlSurface(session, profile, presenter), presenter);
            ui.Review(window, "panel-scpi-" + Slug(profile.Name), ControlPanelOptions(window));
            window.Close();
            ui.AssertClean();
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void ControlPanel_LoopbackSensorDemoManifest(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var manifest = DeviceManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo"));
            SectionExpansionState.Forget(manifest.Name);
            var transport = new FakeTransport();
            var session = new Session(transport, new Pipeline([]));
            await session.OpenAsync(TestContext.CancellationToken);
            using var panel = ManifestPanel.Attach(session, manifest);
            var window = new ControlPanelWindow(panel.Definition, panel.Surface, panel.Presenter);
            var sizes = ui.SizesFor(window);
            UiReview.Show(window, sizes[0]);
            await panel.Surface.InvokeAsync("measure", null, TestContext.CancellationToken);
            var lines = string.Concat(Enumerable.Range(0, 41).Select(i => DevTerm.Transports.Loopback.LoopbackGenerators.SensorSample(i) + "\n"));
            await transport.PushIncomingAsync(Encoding.ASCII.GetBytes(lines));
            var strip = (DevTerm.UiDefinitions.StripChartState)window.Displays["history"].State;
            Assert.IsTrue(StaTestRunner.PumpUntil(() => strip.SamplesOf("chA").Count == 41, _pumpTimeout));

            ui.Review(window, "panel-manifest-loopback-sensor-demo", ControlPanelOptions(window), sizes);
            await session.CloseAsync(TestContext.CancellationToken);
            window.Close();
            ui.AssertClean();
        });
    }

    /// <summary>What every control panel is allowed: color swatches are whatever color they show.</summary>
    private static LayoutCheckOptions ControlPanelOptions(ControlPanelWindow? window) => new()
    {
        Allowed =
        [
            new(WpfLayoutAssert.LightChromeCheck, e => (e is Border border && window?.ColorSwatches.Values.Contains(border) == true) || IsInsideLiveDisplay(e), "A color swatch or a chart paints the color it shows."),
        ],
    };

    /// <summary>
    /// The Light theme's Accent (#4682B4, 4.1:1 on white) and Warning (#B8860B, 3.3:1) roles are
    /// below 4.5:1 as text colors - Playback's sent lines and notes use them. That's the shared theme
    /// palette (both front ends, docs/design/theming.md), not this window's layout, so it's left for a
    /// palette decision rather than patched here. Only those two colors, only in playback lines.
    /// </summary>
    private static LayoutCheckOptions PaletteContrastAllowance => new()
    {
        Allowed =
        [
            new(
                WpfLayoutAssert.ContrastCheck,
                e => e is TextBlock { Name: "LineText", Foreground: System.Windows.Media.SolidColorBrush brush }
                    && !ActiveTheme.Current.IsDark
                    && (brush.Color == WpfTheme.ToColor(ActiveTheme.Current[ThemeRole.Accent]) || brush.Color == WpfTheme.ToColor(ActiveTheme.Current[ThemeRole.Warning])),
                "Light theme Accent/Warning text contrast is a palette decision (see BACKLOG)."),
        ],
    };

    private static bool IsInsideLiveDisplay(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is LiveDisplayElement)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- Dialogs

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void ColorPickerWindow(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var window = new ColorPickerWindow(0xFF, 0x66, 0x00);
            ui.Review(window, "color-picker", new LayoutCheckOptions
            {
                Allowed = [new(WpfLayoutAssert.LightChromeCheck, e => e.Name == "PreviewSwatch", "The preview swatch shows the picked color.")],
            });
            window.Close();
            ui.AssertClean();
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow("light", false)]
    [DataRow("dark", false)]
    [DataRow("light", true)]
    [DataRow("dark", true)]
    public void ManifestPickerWindow_ListAndPathOnly_WithAnError(string theme, bool pathOnly)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var installed = Path.Combine(AppContext.BaseDirectory, "manifests");
            IReadOnlyList<ManifestEntry> entries = pathOnly
                ? []
                : [.. Directory.GetDirectories(installed).Select(d => new ManifestEntry(Path.GetFileName(d), d, "installed")), new ManifestEntry("A manifest with a rather long name for its list row", installed, "user")];
            var window = new ManifestPickerWindow(entries, pathOnly);
            window.PathBox.Text = pathOnly ? string.Empty : Path.Combine(installed, "does-not-exist", "device.json");
            if (pathOnly)
            {
                window.ManifestList.SelectedItem = null;
            }

            Assert.IsFalse(window.TryLoadChoice());
            ui.Review(window, pathOnly ? "manifest-picker-path-only" : "manifest-picker");
            window.Close();
            ui.AssertClean();
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void ScpiInstrumentPickerWindow(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            var window = new ScpiInstrumentPickerWindow();
            ui.Review(window, "scpi-picker");
            window.Close();
            ui.AssertClean();
            await Task.CompletedTask;
        });
    }

    // ---------------------------------------------------------------- ManifestEditorWindow

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void ManifestEditorWindow_EachOutlineNodeKind(string theme)
    {
        var userDirectory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var ui = new UiReview(theme);
                var installed = Path.Combine(AppContext.BaseDirectory, "manifests");
                var editor = new ManifestEditorViewModel(userDirectory, installed);
                Assert.IsTrue(editor.Open(Path.Combine(installed, "loopback-sensor-demo")), editor.StatusMessage);
                var window = new ManifestEditorWindow(editor);
                var sizes = ui.SizesFor(window);
                UiReview.Show(window, sizes[0]);

                foreach (var kind in Enum.GetValues<ManifestNodeKind>())
                {
                    var node = window.Editor.Nodes.FirstOrDefault(n => n.Kind == kind);
                    Assert.IsNotNull(node, $"The demo manifest has no {kind} node to review.");
                    window.OutlineList.SelectedItem = node;
                    UiReview.Settle(window);
                    ui.Review(window, "manifest-editor-" + kind.ToString().ToLowerInvariant(), ControlPanelOptions(window.Preview), sizes);
                }

                window.Editor.ConfirmDiscardChanges = () => true;
                window.Close();
                ui.AssertClean();
                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(userDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------- Playback and Stream Monitor

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void PlaybackWindow_PartWayThroughWithANote(string theme)
    {
        var directory = CreateTempDirectory();
        try
        {
            StaTestRunner.Run(async () =>
            {
                var ui = new UiReview(theme);
                var controller = new PlaybackPresenters().Open(PlaybackWindowTests.WriteSampleLog(directory), new ManualTimeProvider());
                var window = new PlaybackWindow(controller);
                var sizes = ui.SizesFor(window);
                UiReview.Show(window, sizes[0]);
                window.Do(() => controller.SetPresenters(["ascii", "hex"]));
                window.Do(() => controller.SeekTo(5));
                window.NoteBox.Text = "IDN reply is correct";
                window.AddNote();
                window.Do(controller.Step);

                ui.Review(window, "playback", PaletteContrastAllowance, sizes);
                window.Close();
                ui.AssertClean();
                await Task.CompletedTask;
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("light")]
    [DataRow("dark")]
    public void StreamMonitorWindow_WithCaptures(string theme)
    {
        StaTestRunner.Run(async () =>
        {
            var ui = new UiReview(theme);
            await using var bench = await StreamMonitorBench.StartAsync("Rigol DG1062Z");
            await bench.CaptureAsync(StreamContentSamples.Hpgl());
            bench.Time.Advance(TimeSpan.FromSeconds(38));
            await bench.CaptureAsync([.. StreamContentSamples.ScpiBlock(StreamMonitorWindowTests.ScopeScreenPng()), (byte)'\n']);

            var window = new StreamMonitorWindow(bench.Monitor);
            ui.Review(window, "stream-monitor", new LayoutCheckOptions
            {
                Allowed = [new(WpfLayoutAssert.LightChromeCheck, e => e is System.Windows.Controls.Image, "The preview shows the captured image as it is.")],
            });
            window.CaptureList.SelectedIndex = 0;
            ui.Review(window, "stream-monitor-hpgl");
            window.Close();
            ui.AssertClean();
        });
    }

    /// <summary>A file-name-safe short form of a profile name: its first few words.</summary>
    private static string Slug(string name)
    {
        var words = name.ToLowerInvariant().Split([' ', '/', '(', ')', ',', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join('-', words.Take(3).Select(w => string.Concat(w.Where(char.IsLetterOrDigit))));
    }
}
