using System.Collections.ObjectModel;
using System.Drawing;
using System.IO.Ports;
using DevTerm.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

namespace DevTerm.Console;

/// <summary>
/// Shown instead of hard-failing when the bound <see cref="CliOptions"/> doesn't validate (see
/// <see cref="CliOptionsValidator"/>) and the TUI is the active mode — lets the user pick a saved
/// connection profile (<see cref="ConnectionProfileStore"/>) or fill in a transport's fields by
/// hand, and optionally save the result as a new named profile, or import/export one as a
/// standalone file. See docs/design/connection-profiles.md.
/// </summary>
/// <remarks>
/// A first stub, not the full design: there's no live device-manifest picker yet (a manifest is
/// still referenced by typing its name, matching <c>--manifestname</c>), and hiding a transport's
/// irrelevant field group doesn't reflow the layout to close the gap it leaves (Terminal.Gui's
/// <c>Pos.Bottom(view)</c> positioning is computed from a view's frame regardless of its
/// <c>Visible</c> state, so the Presentation/Save/Import-export/Connect section below always sits
/// where it would if every group were shown). See docs/design/frontends.md's startup/configure
/// flow note for the target.
///
/// All connect/load/save/import/export logic lives in the shared <see cref="ConnectionEditorViewModel"/>
/// (also used by WPF's <c>DeviceProfilesWindow</c> via XAML command bindings) — this class only
/// builds Terminal.Gui controls and copies values to/from the view model around each button press,
/// since Terminal.Gui has no data-binding system of its own to do that automatically the way WPF's
/// <c>{Binding ...}</c> does.
/// </remarks>
public static class ConfigureMode
{
    /// <summary>Terminal.Gui's <c>OptionSelector&lt;TEnum&gt;</c> needs an enum (its <c>Values</c> are derived from <c>Enum.GetValues&lt;TEnum&gt;()</c> and can't be set directly) — <see cref="ConnectionEditorViewModel.Transport"/> is a plain string shared with WPF, so this exists purely to drive that one Terminal.Gui widget.</summary>
    internal enum TransportChoice
    {
        Serial,
        Tcp,
        Hid,
    }

    /// <summary>Same reasoning as <see cref="TransportChoice"/>, for <see cref="ConnectionEditorViewModel.Presenter"/>.</summary>
    internal enum PresenterChoice
    {
        Ascii,
        Utf8,
        Hex,
        Decimal,
        Octal,
        Binary,
    }

    /// <returns>Valid <see cref="CliOptions"/> once the user presses Connect with something that validates; <see langword="null"/> if they quit instead.</returns>
    public static CliOptions? Run(CliOptions initial, string? validationError)
    {
        Application.Init();
        try
        {
            var parts = BuildWindow(initial, validationError, new ConnectionProfileStore());
            Application.Run(parts.Window);
            return parts.Result;
        }
        finally
        {
            Application.Shutdown();
        }
    }

    /// <summary>
    /// Builds the window and wires it up, without touching <c>Application.Init</c>/<c>Run</c>/
    /// <c>Shutdown</c> — split out so tests can drive the same production controls headlessly (see
    /// <c>DevTerm.Console.Tests.ConfigureModeTests</c>), the same seam <see cref="TuiMode.BuildWindow"/>
    /// provides. <paramref name="profileStore"/> is a parameter (rather than constructed here)
    /// so tests can point it at a temp directory instead of the real <c>~/.dev-term/profiles</c>.
    /// </summary>
    internal static ConfigureWindowParts BuildWindow(CliOptions initial, string? validationError, ConnectionProfileStore profileStore)
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
        // full form (~32 rows) is routinely taller than a small terminal window. ContentHeight is a
        // generous fixed estimate covering every field group, not computed from an actual layout
        // pass (Terminal.Gui doesn't have positions resolved to concrete rows until Application.Begin
        // runs, well after this method returns) - needs bumping if a future field group makes the
        // form taller still. Confirmed via a real headless probe against the installed Terminal.Gui
        // package that SetContentSize + ViewportSettings actually scrolls (View has no built-in
        // Command.ScrollDown/PageDown implementation to invoke instead - checked directly, neither
        // moved the viewport - so PageUp/PageDown/arrow keys and the mouse wheel are wired by hand
        // below).
        const int ContentHeight = 34;
        var formContent = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        formContent.SetContentSize(new Size(100, ContentHeight));
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
        };
        profilesList.SetSource(new ObservableCollection<string>(viewModel.Profiles));

        var loadButton = new Button { X = Pos.Right(profilesList) + 1, Y = Pos.Top(profilesList), Text = "Load" };
        var deleteButton = new Button { X = Pos.Right(loadButton) + 1, Y = Pos.Top(profilesList), Text = "Delete" };
        var refreshButton = new Button { X = Pos.Right(deleteButton) + 1, Y = Pos.Top(profilesList), Text = "Refresh" };

        var transportLabel = new Label { X = 0, Y = Pos.Bottom(profilesList) + 1, Text = "Transport:" };
        var transportSelector = new OptionSelector<TransportChoice>
        {
            X = Pos.Right(transportLabel) + 1,
            Y = Pos.Top(transportLabel),
            Orientation = Orientation.Horizontal,
            HorizontalSpace = 2,
        };

        var descriptionLabel = new Label { X = 0, Y = Pos.Bottom(transportLabel) + 1, Text = "Description:" };
        var descriptionField = new TextField { X = Pos.Right(descriptionLabel) + 1, Y = Pos.Top(descriptionLabel), Width = 40 };

        var portLabel = new Label { X = 0, Y = Pos.Bottom(descriptionLabel) + 1, Text = "Serial port:" };
        var portField = new TextField { X = Pos.Right(portLabel) + 1, Y = Pos.Top(portLabel), Width = 12, Text = initial.Port ?? string.Empty };
        var baudLabel = new Label { X = Pos.Right(portField) + 3, Y = Pos.Top(portLabel), Text = "Baud:" };
        var baudField = new TextField { X = Pos.Right(baudLabel) + 1, Y = Pos.Top(portLabel), Width = 10, Text = initial.Baud.ToString() };

        var dataBitsLabel = new Label { X = 0, Y = Pos.Bottom(portLabel) + 1, Text = "Data bits:" };
        var dataBitsField = new TextField { X = Pos.Right(dataBitsLabel) + 1, Y = Pos.Top(dataBitsLabel), Width = 4, Text = initial.DataBits.ToString() };
        var parityLabel = new Label { X = Pos.Right(dataBitsField) + 3, Y = Pos.Top(dataBitsLabel), Text = "Parity:" };
        var paritySelector = new OptionSelector<Parity> { X = Pos.Right(parityLabel) + 1, Y = Pos.Top(dataBitsLabel), Orientation = Orientation.Horizontal, HorizontalSpace = 2 };

        var stopBitsLabel = new Label { X = 0, Y = Pos.Bottom(dataBitsLabel) + 1, Text = "Stop bits:" };
        var stopBitsSelector = new OptionSelector<StopBits> { X = Pos.Right(stopBitsLabel) + 1, Y = Pos.Top(stopBitsLabel), Orientation = Orientation.Horizontal, HorizontalSpace = 2 };

        var hostLabel = new Label { X = 0, Y = Pos.Bottom(stopBitsLabel) + 1, Text = "TCP host:" };
        var hostField = new TextField { X = Pos.Right(hostLabel) + 1, Y = Pos.Top(hostLabel), Width = 20, Text = initial.Host ?? string.Empty };
        var tcpPortLabel = new Label { X = Pos.Right(hostField) + 3, Y = Pos.Top(hostLabel), Text = "Port:" };
        var tcpPortField = new TextField { X = Pos.Right(tcpPortLabel) + 1, Y = Pos.Top(hostLabel), Width = 8, Text = initial.TcpPort.ToString() };
        var listenCheckBox = new CheckBox { X = Pos.Right(tcpPortField) + 3, Y = Pos.Top(hostLabel), Text = "Listen", Value = initial.Listen ? CheckState.Checked : CheckState.UnChecked };

        var hidVendorLabel = new Label { X = 0, Y = Pos.Bottom(hostLabel) + 1, Text = "HID vendor ID (decimal):" };
        var hidVendorField = new TextField { X = Pos.Right(hidVendorLabel) + 1, Y = Pos.Top(hidVendorLabel), Width = 10, Text = initial.HidVendorId.ToString() };
        var hidProductLabel = new Label { X = Pos.Right(hidVendorField) + 3, Y = Pos.Top(hidVendorLabel), Text = "Product ID:" };
        var hidProductField = new TextField { X = Pos.Right(hidProductLabel) + 1, Y = Pos.Top(hidVendorLabel), Width = 10, Text = initial.HidProductId.ToString() };

        var presenterLabel = new Label { X = 0, Y = Pos.Bottom(hidVendorLabel) + 1, Text = "Presenter:" };
        var presenterSelector = new OptionSelector<PresenterChoice>
        {
            X = Pos.Right(presenterLabel) + 1,
            Y = Pos.Top(presenterLabel),
            Orientation = Orientation.Horizontal,
            HorizontalSpace = 2,
        };

        var lineEndingLabel = new Label { X = 0, Y = Pos.Bottom(presenterLabel) + 1, Text = "Line ending:" };
        var lineEndingSelector = new OptionSelector<LineEnding>
        {
            X = Pos.Right(lineEndingLabel) + 1,
            Y = Pos.Top(lineEndingLabel),
            Orientation = Orientation.Horizontal,
            HorizontalSpace = 2,
        };

        var saveNameLabel = new Label { X = 0, Y = Pos.Bottom(lineEndingLabel) + 1, Text = "Save as profile named:" };
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

        var connectButton = new Button { X = 0, Y = Pos.Bottom(browseButton) + 1, Text = "Connect", IsDefault = true };
        var quitButton = new Button { X = Pos.Right(connectButton) + 2, Y = Pos.Top(connectButton), Text = "Quit" };

        var parts = new ConfigureWindowParts
        {
            ViewModel = viewModel,
            Window = window,
            ErrorLabel = errorLabel,
            ProfilesList = profilesList,
            LoadButton = loadButton,
            DeleteButton = deleteButton,
            RefreshButton = refreshButton,
            TransportSelector = transportSelector,
            DescriptionField = descriptionField,
            PortField = portField,
            BaudField = baudField,
            DataBitsField = dataBitsField,
            ParitySelector = paritySelector,
            StopBitsSelector = stopBitsSelector,
            HostField = hostField,
            TcpPortField = tcpPortField,
            ListenCheckBox = listenCheckBox,
            HidVendorField = hidVendorField,
            HidProductField = hidProductField,
            PresenterSelector = presenterSelector,
            LineEndingSelector = lineEndingSelector,
            SaveNameField = saveNameField,
            SaveButton = saveButton,
            PathField = pathField,
            BrowseButton = browseButton,
            ImportButton = importButton,
            ExportButton = exportButton,
            ConnectButton = connectButton,
            QuitButton = quitButton,
        };

        // Only the fields for the currently-selected transport are relevant - showing all three
        // groups at once regardless of selection was confusing (a real complaint, not a guess).
        void UpdateTransportVisibility(TransportChoice selected)
        {
            portLabel.Visible = portField.Visible = baudLabel.Visible = baudField.Visible = selected == TransportChoice.Serial;
            dataBitsLabel.Visible = dataBitsField.Visible = parityLabel.Visible = paritySelector.Visible = selected == TransportChoice.Serial;
            stopBitsLabel.Visible = stopBitsSelector.Visible = selected == TransportChoice.Serial;
            hostLabel.Visible = hostField.Visible = tcpPortLabel.Visible = tcpPortField.Visible = listenCheckBox.Visible = selected == TransportChoice.Tcp;
            hidVendorLabel.Visible = hidVendorField.Visible = hidProductLabel.Visible = hidProductField.Visible = selected == TransportChoice.Hid;
        }

        // Terminal.Gui has no data-binding system, so fields are copied to/from the shared view
        // model explicitly around each button press, rather than staying continuously in sync the
        // way WPF's {Binding ...} does for DeviceProfilesWindow.
        void PushFieldsIntoViewModel()
        {
            viewModel.Transport = (transportSelector.Value ?? TransportChoice.Serial).ToString().ToLowerInvariant();
            viewModel.Description = descriptionField.Text;
            viewModel.Port = portField.Text;
            viewModel.Baud = baudField.Text;
            viewModel.DataBits = dataBitsField.Text;
            viewModel.ParityText = (paritySelector.Value ?? Parity.None).ToString();
            viewModel.StopBitsText = (stopBitsSelector.Value ?? StopBits.One).ToString();
            viewModel.Host = hostField.Text;
            viewModel.TcpPort = tcpPortField.Text;
            viewModel.Listen = listenCheckBox.Value == CheckState.Checked;
            viewModel.HidVendorId = hidVendorField.Text;
            viewModel.HidProductId = hidProductField.Text;
            viewModel.Presenter = (presenterSelector.Value ?? PresenterChoice.Hex).ToString().ToLowerInvariant();
            viewModel.LineEndingText = (lineEndingSelector.Value ?? DevTerm.Configuration.LineEnding.None).ToString();
            viewModel.SaveName = saveNameField.Text;
            viewModel.ImportExportPath = pathField.Text;
        }

        void PullFieldsFromViewModel()
        {
            var transportChoice = Enum.TryParse<TransportChoice>(viewModel.Transport, ignoreCase: true, out var t) ? t : TransportChoice.Serial;
            transportSelector.Value = transportChoice;
            descriptionField.Text = viewModel.Description;
            portField.Text = viewModel.Port;
            baudField.Text = viewModel.Baud;
            dataBitsField.Text = viewModel.DataBits;
            paritySelector.Value = Enum.TryParse<Parity>(viewModel.ParityText, ignoreCase: true, out var parity) ? parity : Parity.None;
            stopBitsSelector.Value = Enum.TryParse<StopBits>(viewModel.StopBitsText, ignoreCase: true, out var stopBits) ? stopBits : StopBits.One;
            hostField.Text = viewModel.Host;
            tcpPortField.Text = viewModel.TcpPort;
            listenCheckBox.Value = viewModel.Listen ? CheckState.Checked : CheckState.UnChecked;
            hidVendorField.Text = viewModel.HidVendorId;
            hidProductField.Text = viewModel.HidProductId;
            presenterSelector.Value = Enum.TryParse<PresenterChoice>(viewModel.Presenter, ignoreCase: true, out var p) ? p : PresenterChoice.Hex;
            lineEndingSelector.Value = Enum.TryParse<DevTerm.Configuration.LineEnding>(viewModel.LineEndingText, ignoreCase: true, out var le) ? le : DevTerm.Configuration.LineEnding.None;
            saveNameField.Text = viewModel.SaveName;
            errorLabel.Text = viewModel.StatusMessage;
            profilesList.SetSource(new ObservableCollection<string>(viewModel.Profiles));
            UpdateTransportVisibility(transportChoice);
        }

        PullFieldsFromViewModel();

        transportSelector.ValueChanged += (_, _) => UpdateTransportVisibility(transportSelector.Value ?? TransportChoice.Serial);

        // The TUI's own "overwrite '{name}'?" confirmation - Terminal.Gui's MessageBox.Query is the
        // equivalent of the WPF window's MessageBox.Show wiring for the same ConfirmOverwrite hook.
        viewModel.ConfirmOverwrite = name =>
            MessageBox.Query(Application.Instance!, "dev-term", $"A profile named '{name}' already exists. Overwrite it?", ["Yes", "No"]) == 0;
        viewModel.ConfirmDiscardChanges = () =>
            MessageBox.Query(Application.Instance!, "dev-term", "You have unsaved changes. Close without saving?", ["Yes", "No"]) == 0;

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
                Application.Invoke(() =>
                {
                    viewModel.RefreshCommand.Execute(null);
                    PullFieldsFromViewModel();
                });
            }
            catch (NotInitializedException)
            {
            }
        };
        window.Disposing += (_, _) => viewModel.Dispose();

        viewModel.CloseRequested += (_, _) =>
        {
            parts.Result = viewModel.Result;
            Application.RequestStop();
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
            PullFieldsFromViewModel();
        }

        loadButton.Accepting += (_, e) =>
        {
            LoadSelectedProfile();
            e.Handled = true;
        };

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

        deleteButton.Accepting += (_, e) =>
        {
            SelectProfileIntoViewModel();
            viewModel.DeleteCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

        refreshButton.Accepting += (_, e) =>
        {
            viewModel.RefreshCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

        saveButton.Accepting += (_, e) =>
        {
            PushFieldsIntoViewModel();
            viewModel.SaveCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

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
            Application.Run(dialog);
            if (!dialog.Canceled && dialog.FilePaths.Count > 0)
            {
                pathField.Text = dialog.FilePaths[0];
            }

            e.Handled = true;
        };

        importButton.Accepting += (_, e) =>
        {
            PushFieldsIntoViewModel();
            viewModel.ImportCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

        exportButton.Accepting += (_, e) =>
        {
            PushFieldsIntoViewModel();
            viewModel.ExportCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

        connectButton.Accepting += (_, e) =>
        {
            PushFieldsIntoViewModel();
            viewModel.ConnectCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

        quitButton.Accepting += (_, e) =>
        {
            // Pushed first so a field typed but never sent through Save/Import/Export/Connect (the
            // only buttons that otherwise sync Terminal.Gui's controls into the view model) still
            // counts as dirty here - otherwise Quit could see IsDirty == false purely because the
            // view model was never told about an edit that's actually sitting unsaved on screen.
            PushFieldsIntoViewModel();
            if (!viewModel.ConfirmClose())
            {
                e.Handled = true;
                return;
            }

            parts.Result = null;
            e.Handled = true;
            Application.RequestStop();
        };

        formContent.Add(
            errorLabel, profilesLabel, profilesList, loadButton, deleteButton, refreshButton,
            transportLabel, transportSelector,
            descriptionLabel, descriptionField,
            portLabel, portField, baudLabel, baudField,
            dataBitsLabel, dataBitsField, parityLabel, paritySelector, stopBitsLabel, stopBitsSelector,
            hostLabel, hostField, tcpPortLabel, tcpPortField, listenCheckBox,
            hidVendorLabel, hidVendorField, hidProductLabel, hidProductField,
            presenterLabel, presenterSelector, lineEndingLabel, lineEndingSelector,
            saveNameLabel, saveNameField, saveButton,
            pathLabel, pathField, browseButton, importButton, exportButton,
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
        // [0, ContentHeight - viewport height] so it can't scroll past either end.
        void ScrollBy(int delta)
        {
            var maxY = Math.Max(0, ContentHeight - formContent.Viewport.Height);
            var newY = Math.Clamp(formContent.Viewport.Y + delta, 0, maxY);
            formContent.Viewport = formContent.Viewport with { Y = newY };
        }

        EventHandler<Key>? scrollOnKey = null;
        scrollOnKey = (_, key) =>
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
        };
        Application.KeyDown += scrollOnKey;
        window.Disposing += (_, _) => Application.KeyDown -= scrollOnKey;

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

/// <summary>The controls a test needs to drive <see cref="ConfigureMode"/> headlessly.</summary>
internal sealed class ConfigureWindowParts
{
    /// <summary>Exposed so tests can stub <see cref="ConnectionEditorViewModel.ConfirmOverwrite"/>/<see cref="ConnectionEditorViewModel.ConfirmDiscardChanges"/> instead of hitting the real, blocking Terminal.Gui <c>MessageBox.Query</c> this class wires them to.</summary>
    public required ConnectionEditorViewModel ViewModel { get; init; }

    public required Window Window { get; init; }

    public required Label ErrorLabel { get; init; }

    public required ListView ProfilesList { get; init; }

    public required Button LoadButton { get; init; }

    public required Button DeleteButton { get; init; }

    public required Button RefreshButton { get; init; }

    public required OptionSelector<ConfigureMode.TransportChoice> TransportSelector { get; init; }

    public required TextField DescriptionField { get; init; }

    public required TextField PortField { get; init; }

    public required TextField BaudField { get; init; }

    public required TextField DataBitsField { get; init; }

    public required OptionSelector<Parity> ParitySelector { get; init; }

    public required OptionSelector<StopBits> StopBitsSelector { get; init; }

    public required TextField HostField { get; init; }

    public required TextField TcpPortField { get; init; }

    public required CheckBox ListenCheckBox { get; init; }

    public required TextField HidVendorField { get; init; }

    public required TextField HidProductField { get; init; }

    public required OptionSelector<ConfigureMode.PresenterChoice> PresenterSelector { get; init; }

    public required OptionSelector<LineEnding> LineEndingSelector { get; init; }

    public required TextField SaveNameField { get; init; }

    public required Button SaveButton { get; init; }

    public required TextField PathField { get; init; }

    public required Button BrowseButton { get; init; }

    public required Button ImportButton { get; init; }

    public required Button ExportButton { get; init; }

    public required Button ConnectButton { get; init; }

    public required Button QuitButton { get; init; }

    /// <summary>Set once the user presses Connect (to the validated options) or Quit (to <see langword="null"/>); <see langword="null"/> until either happens.</summary>
    public CliOptions? Result { get; set; }
}
