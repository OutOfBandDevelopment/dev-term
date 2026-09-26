using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives a real <see cref="ConfigureMode"/> window headlessly, the same way
/// <c>TuiModeTests</c> drives <see cref="TuiMode"/> — see <see cref="TuiTestRunner"/>'s doc comment
/// for why headless (no <c>Application.Run()</c> loop) is the mode that supports key injection.
/// Uses a temp directory for <see cref="ConnectionProfileStore"/> rather than the real
/// <c>~/.dev-term/profiles</c>, same isolation as <c>ConnectionProfileStoreTests</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ConfigureModeTests
{
    private static string CreateTempProfilesDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devterm-configuremode-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void RunHeadless(CliOptions initial, string? validationError, ConnectionProfileStore profileStore, Action<ConfigureWindowParts> body) =>
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = ConfigureMode.BuildWindow(app, initial, validationError, profileStore);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);

            try
            {
                body(parts);
            }
            finally
            {
                app.End(token);
            }
        });

    /// <summary>
    /// Simulates pressing a button via <c>View.InvokeCommand(Command.Accept)</c> — a direct,
    /// documented way to invoke a view's command, bypassing key-event routing entirely. Landed on
    /// this after two key-injection-based approaches each proved unreliable against a real running
    /// window: <c>View.SetFocus()</c> makes <c>HasFocus</c> report <see langword="true"/> without
    /// fully registering the view for command routing (a button focused this way then sent an
    /// injected Enter/Space silently did nothing), and Tab-navigating focus onto a button worked in
    /// isolation but not once several tests ran in the same process. This still exercises the real
    /// <c>Accepting</c> handler wired in <see cref="ConfigureMode.BuildWindow"/> — just not via the
    /// full input pipeline, which real end-to-end coverage (a human, or a future OS-level UI
    /// Automation harness) still exercises for the parts this doesn't.
    /// </summary>
    private static void Click(Button button) => button.InvokeCommand(Command.Accept);

    [TestMethod]
    public void FormFields_CanTakeFocus_SoTheUserCanTabToAndTypeIntoThem()
    {
        // A real regression: the form's scrollable container is a plain View, whose CanFocus
        // defaults to false, and an unfocusable container stops focus reaching any child - so no
        // field could be tabbed to or typed into (found by driving the real TUI in a console, not by
        // any test: nothing here previously asked whether a field could hold focus at all). Focus
        // is what's asserted, not typed text: headless key injection is unreliable (see Click), but
        // whether the field is able to hold focus at all is exactly what the bug broke.
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions { Transport = "serial" }, null, new ConnectionProfileStore(directory), parts =>
            {
                parts.DescriptionField.SetFocus();

                Assert.IsTrue(parts.DescriptionField.HasFocus, "The Description field couldn't take focus.");
                Assert.AreSame(parts.DescriptionField, TuiTestRunner.CurrentApp.Navigation?.GetFocused());
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ProfilesChangedExternally_AfterApplicationShutdown_DoesNotCrashTheProcess()
    {
        // A real regression, not a hypothetical: a FileSystemWatcher event that fires (on its own
        // background thread) after Application.Shutdown() has already run - e.g. the window closed
        // and something touches the profiles directory a moment later - used to call
        // Application.Invoke into a fully torn-down Application, throwing NotInitializedException
        // uncaught on that background thread, which crashed the entire test host process rather
        // than just failing one test (confirmed by watching it happen before this was fixed).
        // There's no way to assert "no exception on a background thread" directly from here - the
        // real proof is that this test, and every test after it in the same run, complete normally
        // instead of the whole run aborting.
        var directory = CreateTempProfilesDirectory();
        try
        {
            var app = Application.Create().Init("dotnet");
            try
            {
                var parts = ConfigureMode.BuildWindow(app, new CliOptions(), null, new ConnectionProfileStore(directory));
                var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
                app.LayoutAndDraw(true);
                app.End(token);
            }
            finally
            {
                app.Dispose();
            }

            File.WriteAllText(Path.Combine(directory, "late.json"), "{}");

            // Give the real, still-alive FileSystemWatcher a genuine moment to fire on its
            // background thread before this test (and its temp-directory cleanup) moves on.
            Thread.Sleep(500);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PresenterCheckBoxes_ShowTheInitialPresenters_AndConnectReturnsWhateverIsCheckedAlongsideTheSeparateParser()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23", Presenter = ["ascii", "binary"], Parser = "hex" };
            RunHeadless(initial, null, new ConnectionProfileStore(directory), parts =>
            {
                Assert.AreSequenceEqual(
                    ["ascii", "utf8", "hex", "decimal", "octal", "binary", "k8055", "busylight", "scpi", "radexone", "zoomh4n", "de5000"], [.. parts.PresenterCheckBoxes.Select(c => c.Text.ToString())]);
                Assert.AreSequenceEqual(
                    [true, false, false, false, false, true, false, false, false, false, false, false], [.. parts.PresenterCheckBoxes.Select(c => c.Value == CheckState.Checked)]);
                Assert.AreEqual("hex", parts.ParserSelector.Value, "The send format is its own setting, not tied to the checked presenters.");

                parts.PresenterCheckBoxes[1].Value = CheckState.Checked; // utf8
                parts.PresenterCheckBoxes[0].Value = CheckState.UnChecked; // ascii
                parts.ParserSelector.Value = "decimal";

                Click(parts.ConnectButton);

                Assert.IsNotNull(parts.Result);
                Assert.AreSequenceEqual(["utf8", "binary"], parts.Result.Presenter);
                Assert.AreEqual("decimal", parts.Result.Parser);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Connect_WithNoPresenterChecked_ReportsAndStaysOnTheForm()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23" };
            RunHeadless(initial, null, new ConnectionProfileStore(directory), parts =>
            {
                foreach (var checkBox in parts.PresenterCheckBoxes)
                {
                    checkBox.Value = CheckState.UnChecked;
                }

                Click(parts.ConnectButton);

                Assert.IsNull(parts.Result);
                Assert.Contains("at least one presenter", parts.ErrorLabel.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Connect_WithValidFields_ReturnsOptionsAndStopsTheLoop()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "serial" }; // invalid: no Port
            RunHeadless(initial, "Missing required '--port' for the serial transport.", new ConnectionProfileStore(directory), parts =>
            {
                Assert.Contains("Missing required", parts.ErrorLabel.Text);

                parts.TransportSelector.Value = "tcp";
                parts.HostField.Text = "192.168.0.107";
                parts.TcpPortField.Text = "23";

                Click(parts.ConnectButton);

                Assert.IsNotNull(parts.Result);
                Assert.AreEqual("tcp", parts.Result.Transport);
                Assert.AreEqual("192.168.0.107", parts.Result.Host);
                Assert.AreEqual("23", parts.Result.Port);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Connect_WithStillInvalidFields_ShowsErrorAndDoesNotSetResult()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "tcp" }; // invalid: no Host/Port
            RunHeadless(initial, "Missing or invalid '--port'...", new ConnectionProfileStore(directory), parts =>
            {
                Click(parts.ConnectButton);

                Assert.IsNull(parts.Result);
                Assert.Contains("--port", parts.ErrorLabel.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Quit_SetsResultToNull()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                // Deliberately doesn't edit any fields first: doing so would make the view model
                // dirty, and Quit now asks ConfirmDiscardChanges before proceeding when dirty
                // (wired to a real, blocking Terminal.Gui MessageBox.Query in production) — nothing
                // in headless test mode can click that dialog's button, so the run would hang. The
                // dirty-confirmation logic itself is covered at the view-model level instead (see
                // DevTerm.Configuration.Tests.ConnectionEditorViewModelTests' ConfirmClose_* tests),
                // same convention already used for ConfirmOverwrite's own real dialog.

                // A non-null sentinel first: Result already defaults to null, so clicking Quit and
                // then asserting null would pass even if the click did nothing at all — seeding a
                // non-null value first means the assertion only passes if Quit's handler actually ran.
                parts.Result = new CliOptions();

                Click(parts.QuitButton);

                Assert.IsNull(parts.Result);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void CtrlQ_QuitsLikeTheQuitButton_AskingFirstWhenThereAreUnsavedChanges()
    {
        // The title says "Ctrl+Q to quit", but nothing handled it here: checked in a real console,
        // Ctrl+Q did nothing in the startup editor. RaiseKeyDownEvent, not the key injector - see
        // TuiModeTests.CtrlQ_RequestsStop for why.
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23" }, null, new ConnectionProfileStore(directory), parts =>
            {
                var app = TuiTestRunner.CurrentApp;
                var runnable = (IRunnable)parts.Window;
                var asked = 0;
                parts.ViewModel.ConfirmDiscardChanges = () => { asked++; return false; };
                parts.HostField.Text = "192.168.0.108";
                parts.Result = new CliOptions();

                app.Keyboard.RaiseKeyDownEvent(Key.Q.WithCtrl);

                Assert.AreEqual(1, asked, "An edited form asks before quitting, as the Quit button does.");
                Assert.IsFalse(runnable.StopRequested, "Declining keeps the editor open.");
                Assert.IsNotNull(parts.Result);

                parts.ViewModel.ConfirmDiscardChanges = () => true;
                app.Keyboard.RaiseKeyDownEvent(Key.Q.WithCtrl);

                Assert.IsTrue(runnable.StopRequested, "Confirming closes it.");
                Assert.IsNull(parts.Result, "...without a connection to open.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Enter_InAFormField_PressesConnect_ButInTheProfileListLoadsInstead()
    {
        // Enter in a text field presses the default (Connect) button - checked in a real console
        // too (Enter in Product ID closed the editor and opened the main window). A direct
        // InvokeCommand(Command.Accept) on the field does not show this: only a real key goes through
        // the default-button routing, so this raises one (RaiseKeyDownEvent - see CtrlQ above).
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("saved", new CliOptions { Transport = "tcp", Host = "10.0.0.2", Port = "23" });
            RunHeadless(new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23" }, null, store, parts =>
            {
                var app = TuiTestRunner.CurrentApp;
                parts.ProfilesList.SetFocus();
                parts.ProfilesList.SelectedItem = 0;
                app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                Assert.IsNull(parts.Result, "Enter on a saved profile loads it rather than connecting.");
                Assert.AreEqual("10.0.0.2", parts.HostField.Text);

                parts.HostField.SetFocus();
                app.Keyboard.RaiseKeyDownEvent(Key.Enter);

                Assert.IsNotNull(parts.Result, "Enter in a field connected.");
                Assert.AreEqual("10.0.0.2", parts.Result.Host);
                Assert.IsTrue(((IRunnable)parts.Window).StopRequested);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Quit_WhenDirty_AsksConfirmDiscardChangesAndHonorsTheAnswer()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                // Stubbed rather than left wired to ConfigureMode's real Terminal.Gui
                // MessageBox.Query, which would hang here — nothing in headless test mode can
                // click that dialog's button (see Quit_SetsResultToNull's comment).
                var asked = 0;
                parts.ViewModel.ConfirmDiscardChanges = () => { asked++; return false; };
                parts.HostField.Text = "192.168.0.107";

                // A non-null sentinel first, same reasoning as Quit_SetsResultToNull: proves
                // whether Quit's actual close-and-clear-Result logic ran or not.
                parts.Result = new CliOptions();
                Click(parts.QuitButton);

                Assert.AreEqual(1, asked, "Editing a field first should have made the view model dirty, so Quit should ask before discarding it.");
                Assert.IsNotNull(parts.Result, "Declining the confirmation should stop Quit from actually proceeding.");

                parts.ViewModel.ConfirmDiscardChanges = () => true;
                Click(parts.QuitButton);

                Assert.IsNull(parts.Result, "Confirming discard should let Quit proceed and clear Result.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Save_WritesAProfileTheStoreCanLoadBack()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23", Presenter = ["ascii"] };

            RunHeadless(initial, null, store, parts =>
            {
                parts.SaveNameField.Text = "tek108";
                Click(parts.SaveButton);
                Assert.Contains("Saved profile 'tek108'", parts.ErrorLabel.Text);
            });

            Assert.Contains("tek108", store.List());
            var saved = store.Load("tek108");
            Assert.AreEqual("tcp", saved.Transport);
            Assert.AreEqual("192.168.0.108", saved.Host);
            Assert.AreEqual("23", saved.Port);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadButton_PopulatesFieldsFromTheSelectedProfile()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            // Arrange the saved profile directly through the store rather than by driving the UI a
            // second time in this method: two Application.Init/Shutdown cycles within one test
            // method (rather than one per [TestMethod], MSTest's normal granularity) turned out to
            // leave the second window's button clicks silently doing nothing — found the hard way,
            // not something this stub investigated further given a single-cycle-per-test workaround
            // was straightforward and every test here already needs its own cycle regardless.
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23", Presenter = ["ascii"] });

            RunHeadless(new CliOptions { Transport = "serial" }, "Missing required '--port'...", store, parts =>
            {
                parts.ProfilesList.SelectedItem = 0;
                Click(parts.LoadButton);

                Assert.Contains("Loaded profile 'tek108'", parts.ErrorLabel.Text);
                Assert.AreEqual("tcp", parts.TransportSelector.Value);
                Assert.AreEqual("192.168.0.108", parts.HostField.Text);
                Assert.AreEqual("23", parts.TcpPortField.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DoubleClickingAProfile_LoadsItSameAsTheLoadButton()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23", Presenter = ["ascii"] });

            RunHeadless(new CliOptions { Transport = "serial" }, "Missing required '--port'...", store, parts =>
            {
                parts.ProfilesList.SelectedItem = 0;

                // A double-click maps to Command.Accept on Terminal.Gui's ListView (confirmed via
                // reflection against the installed package — a single click maps to a different
                // command, Activate) — the same InvokeCommand(Command.Accept) mechanism this test
                // class's own Click(Button) helper uses to simulate a button press.
                parts.ProfilesList.InvokeCommand(Command.Accept);

                Assert.Contains("Loaded profile 'tek108'", parts.ErrorLabel.Text);
                Assert.AreEqual("tcp", parts.TransportSelector.Value);
                Assert.AreEqual("192.168.0.108", parts.HostField.Text);
                Assert.AreEqual("23", parts.TcpPortField.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteButton_RemovesTheSelectedProfileFromTheList()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek108", new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23" });

            RunHeadless(new CliOptions(), null, store, parts =>
            {
                parts.ProfilesList.SelectedItem = 0;
                Click(parts.DeleteButton);

                Assert.Contains("Deleted profile 'tek108'", parts.ErrorLabel.Text);
            });

            Assert.DoesNotContain("tek108", store.List());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectingATransport_TogglesWhichFieldGroupIsVisible()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions { Transport = "serial" }, null, new ConnectionProfileStore(directory), parts =>
            {
                Assert.IsTrue(parts.PortField.Visible);
                Assert.IsFalse(parts.HostField.Visible);
                Assert.IsFalse(parts.VendorField.Visible);

                parts.TransportSelector.Value = "tcp";

                Assert.IsFalse(parts.PortField.Visible);
                Assert.IsTrue(parts.HostField.Visible);
                Assert.IsFalse(parts.VendorField.Visible);

                parts.TransportSelector.Value = "hid";

                Assert.IsFalse(parts.PortField.Visible);
                Assert.IsFalse(parts.HostField.Visible);
                Assert.IsTrue(parts.VendorField.Visible);
                Assert.IsTrue(parts.DetectHidButton.Visible);
                Assert.IsFalse(parts.DetectUsbtmcButton.Visible);
                Assert.IsFalse(parts.LoopbackInfoLabel.Visible);

                parts.TransportSelector.Value = "usbtmc";

                Assert.IsFalse(parts.PortField.Visible);
                Assert.IsFalse(parts.HostField.Visible);
                Assert.IsTrue(parts.VendorField.Visible);
                Assert.IsFalse(parts.DetectHidButton.Visible);
                Assert.IsTrue(parts.DetectUsbtmcButton.Visible);
                Assert.IsFalse(parts.LoopbackInfoLabel.Visible);

                parts.TransportSelector.Value = "ble";

                Assert.IsFalse(parts.PortField.Visible);
                Assert.IsFalse(parts.HostField.Visible);
                Assert.IsFalse(parts.VendorField.Visible);
                Assert.IsTrue(parts.BleDeviceIdField.Visible);
                Assert.IsFalse(parts.LoopbackInfoLabel.Visible);

                parts.TransportSelector.Value = "loopback";

                Assert.IsFalse(parts.PortField.Visible);
                Assert.IsFalse(parts.HostField.Visible);
                Assert.IsFalse(parts.VendorField.Visible);
                Assert.IsFalse(parts.BleDeviceIdField.Visible);
                Assert.IsTrue(parts.LoopbackInfoLabel.Visible);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SwitchingTransport_ReflowsTheForm_SoAHiddenGroupLeavesNoGap()
    {
        // The hand-built editor positioned every group with Pos.Bottom(previous), which Terminal.Gui
        // computes from a view's frame whether it's visible or not - so hiding the Serial fields left
        // their whole height blank above Presentation. The generated form re-lays itself out.
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions { Transport = "serial" }, null, new ConnectionProfileStore(directory), parts =>
            {
                var presentation = parts.Form.SectionHeaderLabels["Presentation"];
                var serialTop = presentation.Frame.Y;
                var serialSaveTop = parts.SaveNameField.Frame.Y;

                parts.TransportSelector.Value = "tcp";
                TuiTestRunner.CurrentApp.LayoutAndDraw(true);

                var tcpHeader = parts.Form.SectionHeaderLabels["TCP"];
                Assert.AreEqual(parts.Form.SectionHeaderLabels["Presentation"].Frame.Y, tcpHeader.Frame.Y + 5, "TCP: its header, three rows, then a blank line, then Presentation.");
                Assert.IsLessThan(serialTop, presentation.Frame.Y, "Presentation moved up into the space the Serial fields left.");
                Assert.AreEqual(serialSaveTop - (serialTop - presentation.Frame.Y), parts.SaveNameField.Frame.Y, "The hand-built rows below the form follow it.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Connect_WithLoopbackTransport_AndNoOtherFields_Succeeds()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "serial" }; // invalid: no Port
            RunHeadless(initial, "Missing required '--port' for the serial transport.", new ConnectionProfileStore(directory), parts =>
            {
                parts.TransportSelector.Value = "loopback";

                Click(parts.ConnectButton);

                Assert.IsNotNull(parts.Result);
                Assert.AreEqual("loopback", parts.Result.Transport);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Connect_WithBleTransport_RoundTripsFieldsIntoCliOptions()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var initial = new CliOptions { Transport = "serial" }; // invalid: no Port
            RunHeadless(initial, "Missing required '--port' for the serial transport.", new ConnectionProfileStore(directory), parts =>
            {
                parts.TransportSelector.Value = "ble";
                parts.BleDeviceIdField.Text = "AA:BB:CC:DD:EE:FF";
                parts.BleServiceUuidField.Text = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
                parts.BleWriteUuidField.Text = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
                parts.BleNotifyUuidField.Text = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";

                Click(parts.ConnectButton);

                Assert.IsNotNull(parts.Result);
                Assert.AreEqual("ble", parts.Result.Transport);
                Assert.AreEqual("AA:BB:CC:DD:EE:FF", parts.Result.BleDeviceId);
                Assert.AreEqual("6e400001-b5a3-f393-e0a9-e50e24dcca9e", parts.Result.BleServiceUuid);
                Assert.AreEqual("6e400002-b5a3-f393-e0a9-e50e24dcca9e", parts.Result.BleWriteCharacteristicUuid);
                Assert.AreEqual("6e400003-b5a3-f393-e0a9-e50e24dcca9e", parts.Result.BleNotifyCharacteristicUuid);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportButton_ThenImportButton_RoundTripsFieldsThroughAFile()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var exportPath = Path.Combine(directory, "exported.json");
            var initial = new CliOptions { Transport = "tcp", Host = "192.168.0.108", Port = "23", Presenter = ["ascii"] };

            RunHeadless(initial, null, new ConnectionProfileStore(directory), parts =>
            {
                parts.PathField.Text = exportPath;
                Click(parts.ExportButton);

                Assert.Contains("Exported to", parts.ErrorLabel.Text);
                Assert.IsTrue(File.Exists(exportPath));
            });

            RunHeadless(new CliOptions { Transport = "serial" }, "Missing required '--port'...", new ConnectionProfileStore(directory), parts =>
            {
                parts.PathField.Text = exportPath;
                Click(parts.ImportButton);

                Assert.Contains("Imported", parts.ErrorLabel.Text);
                Assert.AreEqual("tcp", parts.TransportSelector.Value);
                Assert.AreEqual("192.168.0.108", parts.HostField.Text);
                Assert.AreEqual("23", parts.TcpPortField.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportButton_MissingFile_ShowsErrorWithoutThrowing()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions { Transport = "tcp", Host = "192.168.0.107", Port = "23" }, null, new ConnectionProfileStore(directory), parts =>
            {
                parts.PathField.Text = Path.Combine(directory, "does-not-exist.json");
                Click(parts.ImportButton);

                Assert.Contains("Could not import", parts.ErrorLabel.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PageDown_ScrollsToRevealControlsBelowTheFold()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                var before = TuiTestRunner.DumpBuffer();
                Assert.Contains("Stop bits:", before);
                Assert.DoesNotContain("Presenter:", before, "The form is taller than the default window - Presenter and everything after it (pushed one row further down by the HID hex-toggle checkbox) shouldn't be visible before scrolling.");
                Assert.DoesNotContain("Line ending:", before, "The form is taller than the default window - Line ending and everything after it shouldn't be visible before scrolling.");

                // Application.RaiseKeyDownEvent, not the IInputInjector-based TuiTestRunner.PressKey:
                // the injector path is already known unreliable for Application-level (as opposed to
                // focused-view) key handling once several Init/Shutdown cycles have run earlier in
                // the same process - see TuiModeTests.CtrlQ_RequestsStop's own doc comment for the
                // same finding against TuiMode's own global Application.KeyDown handler.
                //
                // Focus is moved off the saved-profiles list first: now that focus really lands
                // there at startup, PageDown is (deliberately) the list's own - see scrollOnKey.
                // Two presses, not one: the form grew by the BLE field group (4 rows), so a single
                // viewport-height PageDown no longer reaches all the way to "Line ending:".
                parts.DescriptionField.SetFocus();
                TuiTestRunner.CurrentApp.Keyboard.RaiseKeyDownEvent(Key.PageDown);
                TuiTestRunner.CurrentApp.Keyboard.RaiseKeyDownEvent(Key.PageDown);
                TuiTestRunner.CurrentApp.LayoutAndDraw(true);

                var after = TuiTestRunner.DumpBuffer();
                Assert.Contains("Line ending:", after, "Expected PageDown to scroll the form down far enough to reveal a control that was below the fold.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FocusingAControlBelowTheFold_ScrollsItIntoView()
    {
        // The other half of the focus fix: once fields could take focus at all, Tab moved focus to a
        // control scrolled out of view and the form stayed put, so the user typed blind.
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions { Transport = "serial" }, null, new ConnectionProfileStore(directory), parts =>
            {
                Assert.DoesNotContain("Import/export file path:", TuiTestRunner.DumpBuffer(), "Precondition: the path field starts below the fold.");

                parts.PathField.SetFocus();
                TuiTestRunner.CurrentApp.LayoutAndDraw(true);

                Assert.Contains("Import/export file path:", TuiTestRunner.DumpBuffer(), "Focusing the path field should have scrolled it into view.");

                parts.DescriptionField.SetFocus();
                TuiTestRunner.CurrentApp.LayoutAndDraw(true);

                Assert.Contains("Description:", TuiTestRunner.DumpBuffer(), "Focusing a field above the viewport should scroll back up to it.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void BrowseButton_IsWiredNextToThePathField()
    {
        // Deliberately doesn't click it: doing so opens a real, native Terminal.Gui OpenDialog via
        // a nested Application.Run(dialog) with nothing able to drive or dismiss it headlessly,
        // which would hang the test - the same "native file/message dialogs are exercised
        // structurally, not by actually opening them" convention already applied to
        // ConfirmOverwrite/ConfirmDiscardChanges's real MessageBox.Query dialogs elsewhere in this
        // class, and matching WPF's own Browse_Click (also untested for the same reason).
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                Assert.AreEqual("Browse...", parts.BrowseButton.Text);
                Assert.IsNotNull(parts.BrowseButton.SuperView, "Expected the button to actually be attached under the window somewhere (not necessarily a direct child - see the scrollable formContent container).");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveAsButton_IsWiredNextToTheExportButton()
    {
        // Same "structural, never actually click it" convention as BrowseButton_IsWiredNextToThePathField
        // above - this one opens a real, native Terminal.Gui SaveDialog via a nested
        // Application.Run(dialog), the save-style counterpart to Browse/OpenDialog (lets you type a
        // brand-new filename that doesn't exist yet, for Export specifically).
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                Assert.AreEqual("Save As...", parts.SaveAsButton.Text);
                Assert.IsNotNull(parts.SaveAsButton.SuperView);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportSelectedButton_WithNothingMarked_ShowsStatusMessage()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                parts.PathField.Text = Path.Combine(directory, "export.zip");
                Click(parts.ExportSelectedButton);

                Assert.Contains("Select one or more saved profiles", parts.ErrorLabel.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportSelectedButton_WithAMarkedProfile_ExportsOnlyThatProfileAsAZip()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            store.Save("other", new CliOptions { Transport = "tcp", Host = "192.168.0.2", Port = "23" });

            RunHeadless(new CliOptions(), null, store, parts =>
            {
                // MarkUnmarkSelectedItem is the same marking mechanism the real SPACE key drives
                // (confirmed against the installed Terminal.Gui v2.5.0 package's ListView docs) -
                // driving it directly here rather than via key injection for the same reliability
                // reason PageDown_ScrollsToRevealControlsBelowTheFold uses Application.RaiseKeyDownEvent
                // instead of TuiTestRunner.PressKey.
                // Profiles list alphabetical (see ConnectionProfileStore.List): "other" then "tek2230".
                parts.ProfilesList.SelectedItem = 1;
                parts.ProfilesList.MarkUnmarkSelectedItem();

                var zipPath = Path.Combine(directory, "export.zip");
                parts.PathField.Text = zipPath;
                Click(parts.ExportSelectedButton);

                Assert.Contains("Exported 1 profile(s)", parts.ErrorLabel.Text);
                Assert.IsTrue(File.Exists(zipPath));

                var importStore = new ConnectionProfileStore(CreateTempProfilesDirectory());
                var result = importStore.ImportZip(zipPath);
                Assert.AreEqual(1, result.Imported);
                Assert.AreSequenceEqual(["tek2230"], [.. importStore.List()]);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportAllButton_ExportsEveryProfileRegardlessOfMarks()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            store.Save("other", new CliOptions { Transport = "tcp", Host = "192.168.0.2", Port = "23" });

            RunHeadless(new CliOptions(), null, store, parts =>
            {
                var zipPath = Path.Combine(directory, "all.zip");
                parts.PathField.Text = zipPath;
                Click(parts.ExportAllButton);

                Assert.Contains("Exported 2 profile(s)", parts.ErrorLabel.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportButton_ZipPath_ImportsProfilesAndRefreshesTheList()
    {
        var sourceDirectory = CreateTempProfilesDirectory();
        var destDirectory = CreateTempProfilesDirectory();
        try
        {
            var source = new ConnectionProfileStore(sourceDirectory);
            source.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            var zipPath = Path.Combine(sourceDirectory, "export.zip");
            source.ExportZip(zipPath, ["tek2230"]);

            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(destDirectory), parts =>
            {
                parts.PathField.Text = zipPath;
                Click(parts.ImportButton);

                Assert.Contains("Imported 1 profile(s)", parts.ErrorLabel.Text);
                Assert.Contains("tek2230", parts.ViewModel.Profiles);
            });
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(destDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void DetectPortButton_IsWiredNextToThePortField()
    {
        // Same "structural, never actually click it" convention as BrowseButton_IsWiredNextToThePathField
        // above - this one opens a nested Application.Run(dialog) too (a plain Dialog+ListView
        // picker, since Terminal.Gui has no built-in combobox - see docs/changes/2026-09-16.md).
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                Assert.AreEqual("Detect...", parts.DetectPortButton.Text);
                Assert.IsNotNull(parts.DetectPortButton.SuperView);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DetectHidButton_IsWiredNextToTheHidIdFields()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                Assert.AreEqual("Detect HID...", parts.DetectHidButton.Text);
                Assert.IsNotNull(parts.DetectHidButton.SuperView);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void IdsShowHexCheckBox_TogglingReformatsTheDisplayedVendorAndProductIdFields()
    {
        // CheckBox.Value flips, then Activating/Activated fire - confirmed via a headless probe
        // against the installed Terminal.Gui v2.5.0 package that Command.Activate (what Space is
        // bound to) is what toggles a CheckBox, not Command.Accept/Select (Accept isn't bound at
        // all for CheckBox's own toggle; Select doesn't exist as a Command in this version).
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(
                new CliOptions { Transport = "hid", VendorId = 1234, ProductId = 49291 },
                null,
                new ConnectionProfileStore(directory),
                parts =>
                {
                    Assert.AreEqual("1234", parts.VendorField.Text);
                    Assert.AreEqual("49291", parts.ProductField.Text);

                    parts.IdsShowHexCheckBox.InvokeCommand(Command.Activate);

                    Assert.AreEqual("04D2", parts.VendorField.Text, "Checking 'Show as hex' should reformat the already-typed value, not require it to be re-entered.");
                    Assert.AreEqual("C08B", parts.ProductField.Text);

                    parts.IdsShowHexCheckBox.InvokeCommand(Command.Activate);

                    Assert.AreEqual("1234", parts.VendorField.Text, "Unchecking should revert back to decimal.");
                    Assert.AreEqual("49291", parts.ProductField.Text);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [TestMethod]
    public void DeleteSelectedButton_WithNothingMarked_ShowsStatusMessage()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            RunHeadless(new CliOptions(), null, new ConnectionProfileStore(directory), parts =>
            {
                Click(parts.DeleteSelectedButton);

                Assert.Contains("Select one or more saved profiles to delete", parts.ErrorLabel.Text);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteSelectedButton_WithMarkedProfiles_DeletesThemAfterConfirmation()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            store.Save("other", new CliOptions { Transport = "tcp", Host = "192.168.0.2", Port = "23" });

            RunHeadless(new CliOptions(), null, store, parts =>
            {
                // The real confirmation is a blocking MessageBox.Query (see the ConfirmOverwrite
                // tests above) - stubbed here, with the asked-about names recorded so the wiring is
                // still proven.
                IReadOnlyList<string>? asked = null;
                parts.ViewModel.ConfirmDeleteProfiles = names =>
                {
                    asked = names;
                    return true;
                };

                // Same marking mechanism (and alphabetical order) as the Export Selected tests above.
                parts.ProfilesList.SelectedItem = 1;
                parts.ProfilesList.MarkUnmarkSelectedItem();
                Click(parts.DeleteSelectedButton);

                Assert.AreSequenceEqual(["tek2230"], [.. asked!]);
                Assert.Contains("Deleted 1 profile(s).", parts.ErrorLabel.Text);
                Assert.AreSequenceEqual(["other"], [.. store.List()]);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteSelectedButton_WhenTheUserDeclines_KeepsTheProfiles()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("tek2230", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });

            RunHeadless(new CliOptions(), null, store, parts =>
            {
                parts.ViewModel.ConfirmDeleteProfiles = _ => false;
                parts.ProfilesList.SelectedItem = 0;
                parts.ProfilesList.MarkUnmarkSelectedItem();
                Click(parts.DeleteSelectedButton);

                Assert.AreEqual("Delete cancelled.", parts.ErrorLabel.Text);
                Assert.AreSequenceEqual(["tek2230"], [.. store.List()]);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [TestMethod]
    public void ReplaceAllButton_AfterConfirmation_ReplacesEverySavedProfileWithTheZip()
    {
        var directory = CreateTempProfilesDirectory();
        try
        {
            var store = new ConnectionProfileStore(directory);
            store.Save("old-one", new CliOptions { Transport = "tcp", Host = "192.168.0.1", Port = "23" });
            var zipPath = Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("new-one.json").Open());
                writer.Write("{ \"Transport\": \"tcp\", \"Host\": \"192.168.0.9\", \"Port\": 23 }");
            }

            try
            {
                RunHeadless(new CliOptions(), null, store, parts =>
                {
                    // The real confirmation is a blocking MessageBox.Query - stubbed, as for Delete Selected.
                    (int, int)? asked = null;
                    parts.ViewModel.ConfirmReplaceAllProfiles = (existing, incoming) =>
                    {
                        asked = (existing, incoming);
                        return true;
                    };

                    parts.PathField.Text = zipPath;
                    Click(parts.ReplaceAllButton);

                    Assert.AreEqual((1, 1), asked);
                    Assert.Contains("Replaced 1 saved profile(s) with 1", parts.ErrorLabel.Text);
                    Assert.AreSequenceEqual(["new-one"], [.. store.List()]);
                });
            }
            finally
            {
                File.Delete(zipPath);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
