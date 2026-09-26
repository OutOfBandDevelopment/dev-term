using System.Collections.ObjectModel;
using System.Drawing;
using DevTerm.Configuration;
using DevTerm.UiDefinitions.Forms;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// Shown instead of hard-failing when the bound <see cref="CliOptions"/> doesn't validate (see
/// <see cref="CliOptionsValidator"/>) and the TUI is the active mode, and from File > Device
/// Profiles... — lets the user pick a saved connection profile (<see cref="ConnectionProfileStore"/>)
/// or fill in a transport's fields by hand, and optionally save the result as a new named profile, or
/// import/export one as a standalone file. See docs/design/connection-profiles.md and
/// docs/specs/connection-editor.md.
/// </summary>
/// <remarks>
/// <para>
/// The connection fields are <em>generated</em>: <see cref="ConnectionEditorViewModel.FormDefinition"/>
/// (made by <see cref="FormDefinitionGenerator"/> from the view model's own annotated properties)
/// rendered by the generic <see cref="FormRenderer"/> and bound two-way to the view model — the same
/// definition WPF's <c>DeviceProfilesWindow</c> renders, so the field list, labels, grouping and
/// "which transport shows which fields" are declared once. A hidden transport's fields no longer
/// leave a gap: the form re-lays itself out on every change. Hand-built around it: the saved-profile
/// list and its buttons, the save-as/import/export rows, Connect/Quit, and the three "Detect..."
/// device pickers (each fills in the generated fields through the view model; the form places them).
/// </para>
/// <para>
/// All connect/load/save/import/export logic lives in the shared <see cref="ConnectionEditorViewModel"/>;
/// this class builds the hand-built controls and forwards their button presses to its commands.
/// </para>
/// </remarks>
public static class ConfigureMode
{
    /// <summary>Every editor window built, so another window's global Ctrl+Q handler can tell it handles that key itself (see <see cref="OwnsQuitKey"/>).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<View, ConfigureWindowParts> _windows = [];

    /// <summary>Whether <paramref name="view"/> is a Connection Editor window - it handles Ctrl+Q itself (as its Quit button), so an outer window's Ctrl+Q handler leaves the key to it.</summary>
    internal static bool OwnsQuitKey(View? view) => view is not null && _windows.TryGetValue(view, out _);
    /// <returns>Valid <see cref="CliOptions"/> once the user presses Connect with something that validates; <see langword="null"/> if they quit instead.</returns>
    public static CliOptions? Run(CliOptions initial, string? validationError)
    {
        var app = Application.Create().Init();
        try
        {
            var parts = BuildWindow(app, initial, validationError, new ConnectionProfileStore());
            app.Run(parts.Window);
            return parts.Result;
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// Builds the window and wires it up, without touching <c>Application.Init</c>/<c>Run</c>/
    /// <c>Shutdown</c> — split out so tests can drive the same production controls headlessly (see
    /// <c>DevTerm.Console.Tests.ConfigureModeTests</c>), the same seam <see cref="TuiMode.BuildWindow"/>
    /// provides. <paramref name="profileStore"/> is a parameter (rather than constructed here)
    /// so tests can point it at a temp directory instead of the real <c>~/.dev-term/profiles</c>.
    /// </summary>
    internal static ConfigureWindowParts BuildWindow(IApplication app, CliOptions initial, string? validationError, ConnectionProfileStore profileStore)
    {
        var viewModel = new ConnectionEditorViewModel(profileStore, initial, validationError);

        var window = new Window
        {
            Title = "dev-term — Configure Connection (Ctrl+Q to quit)",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        // Everything below is added to this scrollable container, not directly to the window - the
        // full form is routinely taller than a small terminal window. Its content height follows the
        // real layout (the Quit button's bottom, re-measured after every layout pass) since the
        // generated form grows and shrinks with the selected transport. Confirmed via a real
        // headless probe against the installed Terminal.Gui package that SetContentSize +
        // ViewportSettings actually scrolls (View has no built-in Command.ScrollDown/PageDown
        // implementation to invoke instead - checked directly, neither moved the viewport - so
        // PageUp/PageDown and the mouse wheel are wired by hand below).
        var contentHeight = 48;
        var formContent = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),

            // A plain View defaults to CanFocus = false, and an unfocusable container stops focus
            // from ever reaching its children - no field, button, or list could be tabbed to or
            // typed into. Found by running the real TUI: the editor took input up to the commit
            // that introduced this container, and none after.
            CanFocus = true,
        };
        formContent.SetContentSize(new Size(Math.Max(app.Screen.Width - 2, 1), contentHeight));
        formContent.ViewportSettings |= ViewportSettingsFlags.AllowNegativeY | ViewportSettingsFlags.HasVerticalScrollBar;

        var errorLabel = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = validationError ?? string.Empty,
        };

        var profilesLabel = new Label { X = 0, Y = Pos.Bottom(errorLabel) + 1, Text = "Saved profiles:" };
        var profilesList = new ListView
        {
            X = 0,
            Y = Pos.Bottom(profilesLabel),
            Width = 30,
            Height = 4,

            // Marking (checkbox-style, since MarkMultiple = true) is what makes "Export Selected"
            // below mean anything - checked directly against the installed Terminal.Gui v2.5.0
            // package that ListWrapper<T> (what SetSource below builds) already implements
            // IsMarked/SetMark itself, so this is the only wiring multi-select needs; the SPACE key
            // toggles a mark regardless of ShowMarks, per that property's own doc comment - ShowMarks
            // just adds the visible checkbox glyph so a user can tell which rows are marked.
            MarkMultiple = true,
            ShowMarks = true,
        };
        profilesList.SetSource(new ObservableCollection<string>(viewModel.Profiles));

        var loadButton = new Button { X = Pos.Right(profilesList) + 1, Y = Pos.Top(profilesList), Text = "Load" };
        var deleteButton = new Button { X = Pos.Right(loadButton) + 1, Y = Pos.Top(profilesList), Text = "Delete" };
        var refreshButton = new Button { X = Pos.Right(deleteButton) + 1, Y = Pos.Top(profilesList), Text = "Refresh" };

        // Own row below the list rather than crowding onto the Load/Delete/Refresh row - the same
        // "doesn't fit an 80-column window without clipping" reasoning already applied to
        // Browse/Import/Export/Save As below.
        var exportSelectedButton = new Button { X = 0, Y = Pos.Bottom(profilesList) + 1, Text = "Export Selected" };
        var exportAllButton = new Button { X = Pos.Right(exportSelectedButton) + 1, Y = Pos.Top(exportSelectedButton), Text = "Export All" };
        var deleteSelectedButton = new Button { X = Pos.Right(exportAllButton) + 1, Y = Pos.Top(exportSelectedButton), Text = "Delete Selected" };

        // The three "pick from what's attached" pickers stay hand-built (each needs the view model's
        // rich device lists and a modal list, which the generic form has no vocabulary for); the
        // generated form still places each one in its row, labels it, and shows/hides it with its
        // transport. Picking writes the view model, whose change notifications refresh the fields.
        var detectPortButton = new Button { Text = "Detect..." };
        var detectHidButton = new Button { Text = "Detect HID..." };
        var detectUsbtmcButton = new Button { Text = "Detect USBTMC..." };
        var formOptions = new TuiFormOptions();
        formOptions.CustomWidgets[nameof(ConnectionEditorViewModel.SelectedSerialPort)] = _ => new TuiCustomWidget(detectPortButton, 2);
        formOptions.CustomWidgets[nameof(ConnectionEditorViewModel.SelectedHidDevice)] = _ => new TuiCustomWidget(detectHidButton, 2);
        formOptions.CustomWidgets[nameof(ConnectionEditorViewModel.SelectedUsbtmcDevice)] = _ => new TuiCustomWidget(detectUsbtmcButton, 2);

        var binding = new FormBinding(viewModel);
        var form = FormRenderer.Build(app, viewModel.FormDefinition, binding, formOptions);
        form.Root.Y = Pos.Bottom(exportSelectedButton) + 1;

        var saveNameLabel = new Label { X = 0, Y = Pos.Bottom(form.Root) + 1, Text = "Save as profile named:" };
        var saveNameField = new TextField { X = Pos.Right(saveNameLabel) + 1, Y = Pos.Top(saveNameLabel), Width = 20 };
        var saveButton = new Button { X = Pos.Right(saveNameField) + 1, Y = Pos.Top(saveNameLabel), Text = "Save Profile" };

        // Browse/Import/Export sit on their own row below the path field, not crowded onto the
        // label's row - the four widgets (path label, a usably-wide field, and three buttons)
        // don't fit in an 80-column window on one row without clipping (found by actually looking
        // at a real captured screenshot after adding Browse, not assumed to fit).
        var pathLabel = new Label { X = 0, Y = Pos.Bottom(saveNameLabel) + 1, Text = "Import/export file path:" };
        var pathField = new TextField { X = Pos.Right(pathLabel) + 1, Y = Pos.Top(pathLabel), Width = 40 };
        var browseButton = new Button { X = 0, Y = Pos.Bottom(pathLabel) + 1, Text = "Browse..." };
        var importButton = new Button { X = Pos.Right(browseButton) + 1, Y = Pos.Top(browseButton), Text = "Import" };
        var exportButton = new Button { X = Pos.Right(importButton) + 1, Y = Pos.Top(browseButton), Text = "Export" };
        var saveAsButton = new Button { X = Pos.Right(exportButton) + 1, Y = Pos.Top(browseButton), Text = "Save As..." };
        var replaceAllButton = new Button { X = Pos.Right(saveAsButton) + 1, Y = Pos.Top(browseButton), Text = "Replace All" };

        var connectButton = new Button { X = 0, Y = Pos.Bottom(browseButton) + 1, Text = "Connect", IsDefault = true };
        var quitButton = new Button { X = Pos.Right(connectButton) + 2, Y = Pos.Top(connectButton), Text = "Quit" };

        TextField Field(string id) => (TextField)form.ControlViews[id];

        var parts = new ConfigureWindowParts
        {
            ViewModel = viewModel,
            Window = window,
            FormContent = formContent,
            Form = form,
            ErrorLabel = errorLabel,
            ProfilesList = profilesList,
            LoadButton = loadButton,
            DeleteButton = deleteButton,
            RefreshButton = refreshButton,
            ExportSelectedButton = exportSelectedButton,
            ExportAllButton = exportAllButton,
            DeleteSelectedButton = deleteSelectedButton,
            TransportSelector = form.Choices[nameof(ConnectionEditorViewModel.Transport)],
            DescriptionField = Field(nameof(ConnectionEditorViewModel.Description)),
            PortField = Field(nameof(ConnectionEditorViewModel.Port)),
            DetectPortButton = detectPortButton,
            BaudField = Field(nameof(ConnectionEditorViewModel.Baud)),
            PortNotFoundLabel = (Label)form.ControlViews[nameof(ConnectionEditorViewModel.SerialPortNotFoundHint)],
            DataBitsField = Field(nameof(ConnectionEditorViewModel.DataBits)),
            ParitySelector = form.Choices[nameof(ConnectionEditorViewModel.ParityText)],
            StopBitsSelector = form.Choices[nameof(ConnectionEditorViewModel.StopBitsText)],
            HandshakeSelector = form.Choices[nameof(ConnectionEditorViewModel.HandshakeText)],
            HostField = Field(nameof(ConnectionEditorViewModel.Host)),
            TcpPortField = Field(nameof(ConnectionEditorViewModel.TcpPort)),
            ListenCheckBox = (CheckBox)form.ControlViews[nameof(ConnectionEditorViewModel.Listen)],
            VendorField = Field(nameof(ConnectionEditorViewModel.VendorIdDisplay)),
            ProductField = Field(nameof(ConnectionEditorViewModel.ProductIdDisplay)),
            DetectHidButton = detectHidButton,
            DetectUsbtmcButton = detectUsbtmcButton,
            SerialNumberField = Field(nameof(ConnectionEditorViewModel.SerialNumber)),
            UsbNotFoundLabel = (Label)form.ControlViews[nameof(ConnectionEditorViewModel.UsbDeviceNotFoundHint)],
            IdsShowHexCheckBox = (CheckBox)form.ControlViews[nameof(ConnectionEditorViewModel.IdsShowHex)],
            LoopbackInfoLabel = (Label)form.ControlViews[nameof(ConnectionEditorViewModel.LoopbackInfo)],
            PresenterCheckBoxes = form.CheckLists[nameof(ConnectionEditorViewModel.PresentersText)],
            ScpiProfileSelector = form.Choices[nameof(ConnectionEditorViewModel.ScpiProfile)],
            ScpiProfilePickButton = form.PickButtons.GetValueOrDefault(nameof(ConnectionEditorViewModel.ScpiProfile)),
            ParserSelector = form.Choices[nameof(ConnectionEditorViewModel.Parser)],
            LineEndingSelector = form.Choices[nameof(ConnectionEditorViewModel.LineEndingText)],
            SaveNameField = saveNameField,
            SaveButton = saveButton,
            PathField = pathField,
            BrowseButton = browseButton,
            ImportButton = importButton,
            ExportButton = exportButton,
            SaveAsButton = saveAsButton,
            ReplaceAllButton = replaceAllButton,
            ConnectButton = connectButton,
            QuitButton = quitButton,
        };

        // The generated fields are bound live; only the hand-built parts are copied around each
        // button press (Terminal.Gui has no data binding of its own).
        saveNameField.TextChanged += (_, _) => viewModel.SaveName = saveNameField.Text;
        pathField.TextChanged += (_, _) => viewModel.ImportExportPath = pathField.Text;

        void PullFromViewModel()
        {
            if (saveNameField.Text != viewModel.SaveName)
            {
                saveNameField.Text = viewModel.SaveName;
            }

            errorLabel.Text = viewModel.StatusMessage;
            profilesList.SetSource(new ObservableCollection<string>(viewModel.Profiles));
        }

        PullFromViewModel();

        // The TUI's own "overwrite '{name}'?" confirmation - Terminal.Gui's MessageBox.Query is the
        // equivalent of the WPF window's MessageBox.Show wiring for the same ConfirmOverwrite hook.
        viewModel.ConfirmOverwrite = name =>
            MessageBox.Query(app, "dev-term", $"A profile named '{name}' already exists. Overwrite it?", ["Yes", "No"]) == 0;
        viewModel.ConfirmDiscardChanges = () =>
            MessageBox.Query(app, "dev-term", "You have unsaved changes. Close without saving?", ["Yes", "No"]) == 0;
        viewModel.ConfirmDeleteProfiles = names =>
            MessageBox.Query(
                app,
                "dev-term",
                names.Count == 1
                    ? $"Delete profile '{names[0]}'? This can't be undone."
                    : $"Delete {names.Count} profiles ({string.Join(", ", names)})? This can't be undone.",
                ["Yes", "No"]) == 0;
        viewModel.ConfirmReplaceAllProfiles = (existing, incoming) =>
            MessageBox.Query(
                app,
                "dev-term",
                $"Delete all {existing} saved profile(s) and import the {incoming} in the zip? This can't be undone.",
                ["Yes", "No"]) == 0;
        viewModel.ResolveZipImportConflict = name =>
            MessageBox.Query(app, "dev-term", $"A profile named '{name}' already exists.", ["Replace", "Rename", "Skip"]) switch
            {
                1 => ZipImportConflictResolution.Rename,
                2 => ZipImportConflictResolution.Skip,
                _ => ZipImportConflictResolution.Replace,
            };

        // The watcher fires on a background thread - Application.Invoke marshals onto the UI loop
        // thread (needs a real Application.Run() loop actively pumping to ever flush - see
        // CLAUDE.md - fine for real use, but means a test exercising this needs RunWithLoop, not
        // RunHeadless). Wrapped in try/catch: found the hard way (crashed a real test run, not
        // theoretical) that a queued FileSystemWatcher event can still fire after
        // Application.Shutdown() has already run - e.g. this exact window's own profiles directory
        // being deleted by test cleanup after the window closed - and Application.Invoke throws
        // NotInitializedException rather than silently no-op-ing in that state, which is fatal on a
        // background thread with nothing to catch it otherwise. Explicit disposal below still
        // covers the normal case; this is the backstop for whatever timing gap let one through.
        viewModel.ProfilesChangedExternally += (_, _) =>
        {
            try
            {
                app.Invoke(() =>
                {
                    viewModel.RefreshCommand.Execute(null);
                    PullFromViewModel();
                });
            }
            catch (NotInitializedException)
            {
            }
        };
        window.Disposing += (_, _) =>
        {
            binding.Dispose();
            viewModel.Dispose();
        };

        viewModel.CloseRequested += (_, _) =>
        {
            parts.Result = viewModel.Result;
            app.RequestStop();
        };

        void SelectProfileIntoViewModel()
        {
            if (profilesList.Source is not null && profilesList.SelectedItem is int index && index >= 0 && index < viewModel.Profiles.Count)
            {
                viewModel.SelectedProfileName = viewModel.Profiles[index];
            }
        }

        // Shared by the Load button and double-clicking a row in the list below - same
        // LoadCommand, not a separate code path, per "double-click should do the same as Load".
        void LoadSelectedProfile()
        {
            SelectProfileIntoViewModel();
            if (viewModel.SelectedProfileName is null)
            {
                errorLabel.Text = "Select a profile first.";
                return;
            }

            viewModel.LoadCommand.Execute(null);
            PullFromViewModel();
        }

        // Runs a view-model command from a hand-built button, then shows its outcome.
        void Run(Button button, Action command)
        {
            button.Accepting += (_, e) =>
            {
                command();
                PullFromViewModel();
                e.Handled = true;
            };
        }

        Run(loadButton, LoadSelectedProfile);

        // ListView's default mouse bindings map a double-click specifically to Command.Accept
        // (a single click maps to Command.Activate instead) - confirmed via reflection against the
        // installed Terminal.Gui v2.5.0 package rather than guessed, since "Activated"/
        // "OpenSelectedItem"-sounding members are a natural but wrong first guess here.
        // View.Accepting (inherited, the same event every Button.Accepting handler above uses) is
        // what actually fires for it.
        profilesList.Accepting += (_, e) =>
        {
            LoadSelectedProfile();
            e.Handled = true;
        };

        Run(deleteButton, () =>
        {
            SelectProfileIntoViewModel();
            viewModel.DeleteCommand.Execute(null);
        });
        Run(refreshButton, () => viewModel.RefreshCommand.Execute(null));
        Run(saveButton, () => viewModel.SaveCommand.Execute(null));

        // Mirrors WPF's own Browse... button: it also just opens a real, native file dialog and
        // sets ImportExportPath from whatever's picked - the one piece of either front end that's
        // still code-behind rather than a shared command, since a native file dialog has no
        // pure-binding/pure-view-model equivalent. Uses OpenDialog specifically (requires an
        // existing file) for both Import and Export, exactly matching WPF's own Browse_Click, which
        // uses OpenFileDialog for both too - you can still hand-edit the picked path afterward for
        // a not-yet-existing export destination.
        browseButton.Accepting += (_, e) =>
        {
            var dialog = new OpenDialog { Path = pathField.Text };
            app.Run(dialog);
            if (!dialog.Canceled && dialog.FilePaths.Count > 0)
            {
                pathField.Text = dialog.FilePaths[0];
            }

            e.Handled = true;
        };

        // The save-style counterpart to Browse: a real Terminal.Gui SaveDialog, which - unlike
        // OpenDialog - lets you type a brand-new filename that doesn't exist yet, for Export
        // specifically (confirmed via a headless probe against the installed v2.5.0 package that
        // SaveDialog.FileName, not .Path, holds the accepted full path once its own "Save" button
        // is clicked). Browse/OpenDialog stays as the picker for Import (an existing file only).
        saveAsButton.Accepting += (_, e) =>
        {
            var dialog = new SaveDialog { Path = pathField.Text };
            app.Run(dialog);
            if (!dialog.Canceled && dialog.FileName is { Length: > 0 } fileName)
            {
                pathField.Text = fileName;
            }

            e.Handled = true;
        };

        detectPortButton.Accepting += (_, e) =>
        {
            var ports = viewModel.SerialPortOptions;
            if (FormRenderer.PickFromList(app, "Detected serial ports", [.. ports.Select(p => p.Display)]) is int i)
            {
                viewModel.SelectedSerialPort = ports[i].Name;
            }

            e.Handled = true;
        };

        // The picker is filtered by the Vendor/Product ID fields (non-zero = must match), which the
        // live-bound form has already written to the view model.
        detectHidButton.Accepting += (_, e) =>
        {
            var devices = viewModel.HidDeviceOptions;
            var index = FormRenderer.PickFromList(
                app,
                "Detected HID devices",
                [.. devices.Select(d => d.Display)],
                viewModel.HidDevicesHiddenByFilter
                    ? "No detected HID device matches the Vendor/Product ID entered (0 means any)."
                    : "Nothing was detected.");
            if (index is int i)
            {
                viewModel.SelectedHidDevice = devices[i];
            }

            e.Handled = true;
        };

        // Same filtering as detectHidButton, against the USBTMC discovery source - the two device
        // lists come from different places even though they write into the same shared fields.
        detectUsbtmcButton.Accepting += (_, e) =>
        {
            var devices = viewModel.UsbtmcDeviceOptions;
            var index = FormRenderer.PickFromList(
                app,
                "Detected USBTMC devices",
                [.. devices.Select(d => d.Display)],
                viewModel.UsbtmcDevicesHiddenByFilter
                    ? "No detected USBTMC device matches the Vendor/Product ID entered (0 means any)."
                    : "Nothing was detected.");
            if (index is int i)
            {
                viewModel.SelectedUsbtmcDevice = devices[i];
            }

            e.Handled = true;
        };

        Run(importButton, () => viewModel.ImportCommand.Execute(null));
        Run(replaceAllButton, () => viewModel.ReplaceAllFromZipCommand.Execute(null));
        Run(exportButton, () => viewModel.ExportCommand.Execute(null));

        // Reads marks directly off profilesList rather than tracking them as marks change (there's
        // no Terminal.Gui event for that - marking is driven by ListView's own SPACE-key command
        // binding) - the same "copy into the view model right before the button's own command runs"
        // pattern SelectProfileIntoViewModel already uses for the single-select case above.
        void PushMarkedProfilesIntoViewModel()
        {
            viewModel.SelectedProfileNames.Clear();
            foreach (var index in profilesList.GetAllMarkedItems())
            {
                if (index >= 0 && index < viewModel.Profiles.Count)
                {
                    viewModel.SelectedProfileNames.Add(viewModel.Profiles[index]);
                }
            }
        }

        Run(exportSelectedButton, () =>
        {
            PushMarkedProfilesIntoViewModel();
            viewModel.ExportSelectedProfilesCommand.Execute(null);
        });
        Run(exportAllButton, () => viewModel.ExportAllProfilesCommand.Execute(null));
        Run(deleteSelectedButton, () =>
        {
            PushMarkedProfilesIntoViewModel();
            viewModel.DeleteSelectedProfilesCommand.Execute(null);
        });
        Run(connectButton, () => viewModel.ConnectCommand.Execute(null));

        void Quit()
        {
            if (!viewModel.ConfirmClose())
            {
                return;
            }

            parts.Result = null;
            app.RequestStop();
        }

        quitButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Quit();
        };

        // Ctrl+Q, as the title advertises - the same as Quit (unsaved-changes prompt included). On
        // the global KeyDown event for the same reason as TuiMode's: a focused field sees keys
        // first. Only while this window is on top (not under a file dialog it opened). Checked in a
        // real console: before this, Ctrl+Q did nothing in the startup editor (only Esc closed it),
        // and from File > Device Profiles... it was TuiMode's handler that closed it, skipping the
        // unsaved-changes prompt - TuiMode now leaves it to this one (see OwnsQuitKey).
        void quitOnCtrlQ(object? _, Key key)
        {
            if (key != Key.Q.WithCtrl || key.Handled || app.TopRunnableView != window)
            {
                return;
            }

            key.Handled = true;
            Quit();
        }

        app.Keyboard.KeyDown += quitOnCtrlQ;
        window.Disposing += (_, _) => app.Keyboard.KeyDown -= quitOnCtrlQ;
        _windows.AddOrUpdate(window, parts);

        formContent.Add(
            errorLabel, profilesLabel, profilesList, loadButton, deleteButton, refreshButton,
            exportSelectedButton, exportAllButton, deleteSelectedButton,
            form.Root,
            saveNameLabel, saveNameField, saveButton,
            pathLabel, pathField, browseButton, importButton, exportButton, saveAsButton, replaceAllButton,
            connectButton, quitButton);
        window.Add(formContent);

        // PageUp/PageDown and the mouse wheel scroll the form when it doesn't fit. PageUp/PageDown
        // bound on the global Application.KeyDown event, not formContent's own KeyDown, for the
        // same reason Ctrl+Q needed the global event elsewhere in this codebase: a per-view KeyDown
        // handler doesn't reliably see a key already routed to a focused child first. Deliberately
        // skipped whenever profilesList has focus, and deliberately not also binding the plain
        // arrow keys: checked directly (ListView.KeyBindings.GetBindings()) that ListView itself
        // already binds PageUp/PageDown *and* CursorUp/CursorDown for its own item navigation - a
        // global intercept would reach Application.KeyDown before ListView's own routing and steal
        // those keys from it entirely whenever the saved-profiles list has focus. Clamped to
        // [0, content height - viewport height] so it can't scroll past either end.
        void ScrollBy(int delta)
        {
            var maxY = Math.Max(0, contentHeight - formContent.Viewport.Height);
            var newY = Math.Clamp(formContent.Viewport.Y + delta, 0, maxY);
            formContent.Viewport = formContent.Viewport with { Y = newY };
        }

        // The content ends at the Quit button - re-measured after each layout pass, since the form
        // above it changes height with the transport - and is exactly as wide as the visible area:
        // nothing here is wider than an 80-column window, and a fixed width (it was 100) clipped
        // the full-width rows (the Loopback note, the error line) on a wider terminal.
        formContent.SubViewsLaidOut += (_, _) =>
        {
            var measured = Math.Max(quitButton.Frame.Bottom + 1, 1);
            var width = Math.Max(formContent.Viewport.Width, 1);
            if (measured != contentHeight || width != formContent.GetContentSize().Width)
            {
                contentHeight = measured;
                formContent.SetContentSize(new Size(width, contentHeight));
                ScrollBy(0);
            }
        };

        // Tabbing (or clicking) onto a control that's scrolled out of view scrolls it into view -
        // without this, focus moved to an off-screen field and the user typed blind.
        void RevealRows(int top, int height)
        {
            var viewport = formContent.Viewport;
            if (top < viewport.Y)
            {
                ScrollBy(top - viewport.Y);
            }
            else if (top + height > viewport.Y + viewport.Height)
            {
                ScrollBy(top + height - (viewport.Y + viewport.Height));
            }
        }

        // Wired per direct child: a container (an option selector's radio buttons) reports HasFocus
        // when any of its own children does, so the direct child's Frame is the right thing to
        // reveal. The generated form reports its own focused row instead (its Frame is the whole form).
        foreach (var child in formContent.SubViews.Where(v => v != form.Root))
        {
            child.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue)
                {
                    RevealRows(child.Frame.Y, child.Frame.Height);
                }
            };
        }

        form.RowFocused += (top, height) => RevealRows(form.Root.Frame.Y + top, height);

        void scrollOnKey(object? _, Key key)
        {
            if (profilesList.HasFocus)
            {
                return;
            }

            var delta = key == Key.PageDown ? formContent.Viewport.Height
                : key == Key.PageUp ? -formContent.Viewport.Height
                : 0;

            if (delta == 0)
            {
                return;
            }

            ScrollBy(delta);
            key.Handled = true;
        }

        app.Keyboard.KeyDown += scrollOnKey;
        window.Disposing += (_, _) => app.Keyboard.KeyDown -= scrollOnKey;

        formContent.MouseEvent += (_, mouse) =>
        {
            if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
            {
                ScrollBy(1);
                mouse.Handled = true;
            }
            else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
            {
                ScrollBy(-1);
                mouse.Handled = true;
            }
        };

        return parts;
    }
}

/// <summary>The controls a test needs to drive <see cref="ConfigureMode"/> headlessly — the generated form's widgets looked up by field id, plus the hand-built parts.</summary>
internal sealed class ConfigureWindowParts
{
    /// <summary>Exposed so tests can stub <see cref="ConnectionEditorViewModel.ConfirmOverwrite"/>/<see cref="ConnectionEditorViewModel.ConfirmDiscardChanges"/> instead of hitting the real, blocking Terminal.Gui <c>MessageBox.Query</c> this class wires them to.</summary>
    public required ConnectionEditorViewModel ViewModel { get; init; }

    public required Window Window { get; init; }

    /// <summary>The scrolling container holding everything; its <c>Viewport</c> is the visible part.</summary>
    public required View FormContent { get; init; }

    /// <summary>The generated connection-field form (see <see cref="FormRenderer"/>).</summary>
    public required TuiFormParts Form { get; init; }

    public required Label ErrorLabel { get; init; }

    public required ListView ProfilesList { get; init; }

    public required Button LoadButton { get; init; }

    public required Button DeleteButton { get; init; }

    public required Button RefreshButton { get; init; }

    public required Button ExportSelectedButton { get; init; }

    public required Button ExportAllButton { get; init; }

    public required Button DeleteSelectedButton { get; init; }

    /// <summary>The transport choice — an <see cref="OptionSelector"/> of the view model's <see cref="ConnectionEditorViewModel.TransportOptions"/>; set <see cref="TuiChoice.Value"/> to pick one as a user would.</summary>
    public required TuiChoice TransportSelector { get; init; }

    public required TextField DescriptionField { get; init; }

    public required TextField PortField { get; init; }

    public required Button DetectPortButton { get; init; }

    public required TextField BaudField { get; init; }

    /// <summary>Visible when <see cref="ConnectionEditorViewModel.ConnectedDeviceNotFound"/> is true for the serial transport.</summary>
    public required Label PortNotFoundLabel { get; init; }

    public required TextField DataBitsField { get; init; }

    public required TuiChoice ParitySelector { get; init; }

    public required TuiChoice StopBitsSelector { get; init; }

    public required TuiChoice HandshakeSelector { get; init; }

    public required TextField HostField { get; init; }

    public required TextField TcpPortField { get; init; }

    public required CheckBox ListenCheckBox { get; init; }

    public required TextField VendorField { get; init; }

    public required TextField ProductField { get; init; }

    public required Button DetectHidButton { get; init; }

    public required Button DetectUsbtmcButton { get; init; }

    /// <summary>Optional - blank means "match the first device found for Vendor/Product ID" (see <see cref="ConnectionEditorViewModel.SerialNumber"/>).</summary>
    public required TextField SerialNumberField { get; init; }

    /// <summary>Visible when <see cref="ConnectionEditorViewModel.ConnectedDeviceNotFound"/> is true for the HID/USBTMC transport.</summary>
    public required Label UsbNotFoundLabel { get; init; }

    public required CheckBox IdsShowHexCheckBox { get; init; }

    /// <summary>Shown only when the loopback transport is selected — it takes no configuration.</summary>
    public required Label LoopbackInfoLabel { get; init; }

    /// <summary>One checkbox per presenter, in <see cref="ConnectionEditorViewModel.PresenterChoices"/> order — the multi-select presenter picker.</summary>
    public required IReadOnlyList<CheckBox> PresenterCheckBoxes { get; init; }

    /// <summary>Shown only when the "scpi" presenter is checked — see <see cref="ConnectionEditorViewModel.IsScpiPresenterSelected"/>. Too many profiles for one line, so a text field plus <see cref="ScpiProfilePickButton"/>.</summary>
    public required TuiChoice ScpiProfileSelector { get; init; }

    public required Button? ScpiProfilePickButton { get; init; }

    /// <summary>The send format (parser) — every presenter can encode input, so its options are <see cref="ConnectionEditorViewModel.PresenterOptions"/>.</summary>
    public required TuiChoice ParserSelector { get; init; }

    public required TuiChoice LineEndingSelector { get; init; }

    public required TextField SaveNameField { get; init; }

    public required Button SaveButton { get; init; }

    public required TextField PathField { get; init; }

    public required Button BrowseButton { get; init; }

    public required Button ImportButton { get; init; }

    public required Button ExportButton { get; init; }

    public required Button SaveAsButton { get; init; }

    public required Button ReplaceAllButton { get; init; }

    public required Button ConnectButton { get; init; }

    public required Button QuitButton { get; init; }

    /// <summary>Set once the user presses Connect (to the validated options) or Quit (to <see langword="null"/>); <see langword="null"/> until either happens.</summary>
    public CliOptions? Result { get; set; }
}
