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
        Usbtmc,
        Loopback,
    }

    /// <summary>Same reasoning as <see cref="TransportChoice"/>, for <see cref="ConnectionEditorViewModel.Parser"/> (the presenter picker itself is checkboxes, not this).</summary>
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
        const int ContentHeight = 46;
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

        var transportLabel = new Label { X = 0, Y = Pos.Bottom(exportSelectedButton) + 1, Text = "Transport:" };
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
        var detectPortButton = new Button { X = Pos.Right(portField) + 1, Y = Pos.Top(portLabel), Text = "Detect..." };
        var baudLabel = new Label { X = Pos.Right(detectPortButton) + 3, Y = Pos.Top(portLabel), Text = "Baud:" };
        var baudField = new TextField { X = Pos.Right(baudLabel) + 1, Y = Pos.Top(portLabel), Width = 10, Text = initial.Baud.ToString() };

        var dataBitsLabel = new Label { X = 0, Y = Pos.Bottom(portLabel) + 1, Text = "Data bits:" };
        var dataBitsField = new TextField { X = Pos.Right(dataBitsLabel) + 1, Y = Pos.Top(dataBitsLabel), Width = 4, Text = initial.DataBits.ToString() };
        var parityLabel = new Label { X = Pos.Right(dataBitsField) + 3, Y = Pos.Top(dataBitsLabel), Text = "Parity:" };
        var paritySelector = new OptionSelector<Parity> { X = Pos.Right(parityLabel) + 1, Y = Pos.Top(dataBitsLabel), Orientation = Orientation.Horizontal, HorizontalSpace = 2 };

        var stopBitsLabel = new Label { X = 0, Y = Pos.Bottom(dataBitsLabel) + 1, Text = "Stop bits:" };
        var stopBitsSelector = new OptionSelector<StopBits> { X = Pos.Right(stopBitsLabel) + 1, Y = Pos.Top(stopBitsLabel), Orientation = Orientation.Horizontal, HorizontalSpace = 2 };

        var handshakeLabel = new Label { X = 0, Y = Pos.Bottom(stopBitsLabel) + 1, Text = "Handshake:" };
        var handshakeSelector = new OptionSelector<Handshake> { X = Pos.Right(handshakeLabel) + 1, Y = Pos.Top(handshakeLabel), Orientation = Orientation.Horizontal, HorizontalSpace = 2 };

        var hostLabel = new Label { X = 0, Y = Pos.Bottom(handshakeLabel) + 1, Text = "TCP host:" };
        var hostField = new TextField { X = Pos.Right(hostLabel) + 1, Y = Pos.Top(hostLabel), Width = 20, Text = initial.Host ?? string.Empty };
        var tcpPortLabel = new Label { X = Pos.Right(hostField) + 3, Y = Pos.Top(hostLabel), Text = "Port:" };
        var tcpPortField = new TextField { X = Pos.Right(tcpPortLabel) + 1, Y = Pos.Top(hostLabel), Width = 8, Text = initial.Port ?? "0" };
        var listenCheckBox = new CheckBox { X = Pos.Right(tcpPortField) + 3, Y = Pos.Top(hostLabel), Text = "Listen", Value = initial.Listen ? CheckState.Checked : CheckState.UnChecked };

        // Shared by the "hid" and "usbtmc" transports — both select a physical USB device the same
        // way (vendor/product ID, optionally a serial number), so one field group serves both;
        // only the Detect... button differs, since HID and USBTMC devices come from different
        // discovery sources.
        var vendorLabel = new Label { X = 0, Y = Pos.Bottom(hostLabel) + 1, Text = "Vendor ID:" };
        var vendorField = new TextField { X = Pos.Right(vendorLabel) + 1, Y = Pos.Top(vendorLabel), Width = 10, Text = initial.VendorId.ToString() };
        var productLabel = new Label { X = Pos.Right(vendorField) + 3, Y = Pos.Top(vendorLabel), Text = "Product ID:" };
        var productField = new TextField { X = Pos.Right(productLabel) + 1, Y = Pos.Top(vendorLabel), Width = 10, Text = initial.ProductId.ToString() };
        var detectHidButton = new Button { X = Pos.Right(productField) + 3, Y = Pos.Top(vendorLabel), Text = "Detect HID..." };
        var detectUsbtmcButton = new Button { X = Pos.Right(detectHidButton) + 1, Y = Pos.Top(vendorLabel), Text = "Detect USBTMC..." };
        var idsShowHexCheckBox = new CheckBox { X = 0, Y = Pos.Bottom(vendorLabel) + 1, Text = "Show as hex" };

        var loopbackInfoLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(idsShowHexCheckBox) + 1,
            Text = "No configuration needed — a scripted fake device. Try \"hello\", \"Send Stream: N, ascii\", \"Send Events: N\", or \"help\"/\"?\".",
        };

        // The presenter picker is multi-select, so a row of checkboxes rather than an OptionSelector
        // (radio buttons, single-select only) - one per PresenterChoices entry, in that order.
        var presenterLabel = new Label { X = 0, Y = Pos.Bottom(loopbackInfoLabel) + 1, Text = "Presenters:" };
        var presenterCheckBoxes = new List<CheckBox>();
        foreach (var choice in viewModel.PresenterChoices)
        {
            presenterCheckBoxes.Add(new CheckBox
            {
                X = presenterCheckBoxes.Count == 0 ? Pos.Right(presenterLabel) + 1 : Pos.Right(presenterCheckBoxes[^1]) + 1,
                Y = Pos.Top(presenterLabel),
                Text = choice.Name,
            });
        }

        // Shown only when the "scpi" presenter checkbox above is checked - preselects a profile so
        // the runtime "SCPI Instrument..." menu item's own picker doesn't need to be re-run every
        // connection (see CliOptions.ScpiProfile). A TextField + picker button rather than a
        // Terminal.Gui combobox, matching the port/HID "type it or Detect..." pattern above - there's
        // no built-in combobox widget (see PickFromList's own doc comment).
        var scpiChoiceIndex = -1;
        for (var i = 0; i < viewModel.PresenterChoices.Count; i++)
        {
            if (viewModel.PresenterChoices[i].Name.Equals("scpi", StringComparison.OrdinalIgnoreCase))
            {
                scpiChoiceIndex = i;
                break;
            }
        }

        var scpiProfileLabel = new Label { X = 0, Y = Pos.Bottom(presenterLabel) + 1, Text = "SCPI profile:" };
        var scpiProfileField = new TextField { X = Pos.Right(scpiProfileLabel) + 1, Y = Pos.Top(scpiProfileLabel), Width = 30 };
        var scpiProfilePickButton = new Button { X = Pos.Right(scpiProfileField) + 1, Y = Pos.Top(scpiProfileLabel), Text = "Pick..." };

        void UpdateScpiProfileVisibility()
        {
            var visible = scpiChoiceIndex >= 0 && presenterCheckBoxes[scpiChoiceIndex].Value == CheckState.Checked;
            scpiProfileLabel.Visible = scpiProfileField.Visible = scpiProfilePickButton.Visible = visible;
        }

        // What encodes a typed line - independent of the presenters above (display only).
        var parserLabel = new Label { X = 0, Y = Pos.Bottom(scpiProfileLabel) + 1, Text = "Send as:" };
        var parserSelector = new OptionSelector<PresenterChoice>
        {
            X = Pos.Right(parserLabel) + 1,
            Y = Pos.Top(parserLabel),
            Orientation = Orientation.Horizontal,
            HorizontalSpace = 2,
        };

        var lineEndingLabel = new Label { X = 0, Y = Pos.Bottom(parserLabel) + 1, Text = "Line ending:" };
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
        var saveAsButton = new Button { X = Pos.Right(exportButton) + 1, Y = Pos.Top(browseButton), Text = "Save As..." };
        var replaceAllButton = new Button { X = Pos.Right(saveAsButton) + 1, Y = Pos.Top(browseButton), Text = "Replace All" };

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
            ExportSelectedButton = exportSelectedButton,
            ExportAllButton = exportAllButton,
            DeleteSelectedButton = deleteSelectedButton,
            TransportSelector = transportSelector,
            DescriptionField = descriptionField,
            PortField = portField,
            DetectPortButton = detectPortButton,
            BaudField = baudField,
            DataBitsField = dataBitsField,
            ParitySelector = paritySelector,
            StopBitsSelector = stopBitsSelector,
            HandshakeSelector = handshakeSelector,
            HostField = hostField,
            TcpPortField = tcpPortField,
            ListenCheckBox = listenCheckBox,
            VendorField = vendorField,
            ProductField = productField,
            DetectHidButton = detectHidButton,
            DetectUsbtmcButton = detectUsbtmcButton,
            IdsShowHexCheckBox = idsShowHexCheckBox,
            LoopbackInfoLabel = loopbackInfoLabel,
            PresenterCheckBoxes = presenterCheckBoxes,
            ScpiProfileField = scpiProfileField,
            ScpiProfilePickButton = scpiProfilePickButton,
            ParserSelector = parserSelector,
            LineEndingSelector = lineEndingSelector,
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

        // Only the fields for the currently-selected transport are relevant - showing all three
        // groups at once regardless of selection was confusing (a real complaint, not a guess).
        void UpdateTransportVisibility(TransportChoice selected)
        {
            portLabel.Visible = portField.Visible = detectPortButton.Visible = baudLabel.Visible = baudField.Visible = selected == TransportChoice.Serial;
            dataBitsLabel.Visible = dataBitsField.Visible = parityLabel.Visible = paritySelector.Visible = selected == TransportChoice.Serial;
            stopBitsLabel.Visible = stopBitsSelector.Visible = selected == TransportChoice.Serial;
            handshakeLabel.Visible = handshakeSelector.Visible = selected == TransportChoice.Serial;
            hostLabel.Visible = hostField.Visible = tcpPortLabel.Visible = tcpPortField.Visible = listenCheckBox.Visible = selected == TransportChoice.Tcp;
            var isUsbDevice = selected == TransportChoice.Hid || selected == TransportChoice.Usbtmc;
            vendorLabel.Visible = vendorField.Visible = productLabel.Visible = productField.Visible = idsShowHexCheckBox.Visible = isUsbDevice;
            detectHidButton.Visible = selected == TransportChoice.Hid;
            detectUsbtmcButton.Visible = selected == TransportChoice.Usbtmc;
            loopbackInfoLabel.Visible = selected == TransportChoice.Loopback;
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
            viewModel.HandshakeText = (handshakeSelector.Value ?? Handshake.None).ToString();
            viewModel.Host = hostField.Text;
            viewModel.TcpPort = tcpPortField.Text;
            viewModel.Listen = listenCheckBox.Value == CheckState.Checked;
            viewModel.IdsShowHex = idsShowHexCheckBox.Value == CheckState.Checked;
            viewModel.VendorIdDisplay = vendorField.Text;
            viewModel.ProductIdDisplay = productField.Text;
            for (var i = 0; i < presenterCheckBoxes.Count; i++)
            {
                viewModel.PresenterChoices[i].IsSelected = presenterCheckBoxes[i].Value == CheckState.Checked;
            }

            viewModel.ScpiProfile = scpiProfileField.Text;
            viewModel.Parser = (parserSelector.Value ?? PresenterChoice.Hex).ToString().ToLowerInvariant();
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
            handshakeSelector.Value = Enum.TryParse<Handshake>(viewModel.HandshakeText, ignoreCase: true, out var handshake) ? handshake : Handshake.None;
            hostField.Text = viewModel.Host;
            tcpPortField.Text = viewModel.TcpPort;
            listenCheckBox.Value = viewModel.Listen ? CheckState.Checked : CheckState.UnChecked;
            idsShowHexCheckBox.Value = viewModel.IdsShowHex ? CheckState.Checked : CheckState.UnChecked;
            vendorField.Text = viewModel.VendorIdDisplay;
            productField.Text = viewModel.ProductIdDisplay;
            for (var i = 0; i < presenterCheckBoxes.Count; i++)
            {
                presenterCheckBoxes[i].Value = viewModel.PresenterChoices[i].IsSelected ? CheckState.Checked : CheckState.UnChecked;
            }

            scpiProfileField.Text = viewModel.ScpiProfile;
            parserSelector.Value = Enum.TryParse<PresenterChoice>(viewModel.Parser, ignoreCase: true, out var p) ? p : PresenterChoice.Hex;
            lineEndingSelector.Value = Enum.TryParse<DevTerm.Configuration.LineEnding>(viewModel.LineEndingText, ignoreCase: true, out var le) ? le : DevTerm.Configuration.LineEnding.None;
            saveNameField.Text = viewModel.SaveName;
            errorLabel.Text = viewModel.StatusMessage;
            profilesList.SetSource(new ObservableCollection<string>(viewModel.Profiles));
            UpdateTransportVisibility(transportChoice);
            UpdateScpiProfileVisibility();
        }

        PullFieldsFromViewModel();

        transportSelector.ValueChanged += (_, _) => UpdateTransportVisibility(transportSelector.Value ?? TransportChoice.Serial);

        if (scpiChoiceIndex >= 0)
        {
            presenterCheckBoxes[scpiChoiceIndex].Activated += (_, _) => UpdateScpiProfileVisibility();
        }

        scpiProfilePickButton.Accepting += (_, e) =>
        {
            var choice = PickFromList("SCPI instrument profile", viewModel.ScpiProfileOptions);
            if (choice is not null)
            {
                scpiProfileField.Text = choice;
            }

            e.Handled = true;
        };

        // The TUI's own "overwrite '{name}'?" confirmation - Terminal.Gui's MessageBox.Query is the
        // equivalent of the WPF window's MessageBox.Show wiring for the same ConfirmOverwrite hook.
        viewModel.ConfirmOverwrite = name =>
            MessageBox.Query(Application.Instance!, "dev-term", $"A profile named '{name}' already exists. Overwrite it?", ["Yes", "No"]) == 0;
        viewModel.ConfirmDiscardChanges = () =>
            MessageBox.Query(Application.Instance!, "dev-term", "You have unsaved changes. Close without saving?", ["Yes", "No"]) == 0;
        viewModel.ConfirmDeleteProfiles = names =>
            MessageBox.Query(
                Application.Instance!,
                "dev-term",
                names.Count == 1
                    ? $"Delete profile '{names[0]}'? This can't be undone."
                    : $"Delete {names.Count} profiles ({string.Join(", ", names)})? This can't be undone.",
                ["Yes", "No"]) == 0;
        viewModel.ConfirmReplaceAllProfiles = (existing, incoming) =>
            MessageBox.Query(
                Application.Instance!,
                "dev-term",
                $"Delete all {existing} saved profile(s) and import the {incoming} in the zip? This can't be undone.",
                ["Yes", "No"]) == 0;
        viewModel.ResolveZipImportConflict = name =>
            MessageBox.Query(Application.Instance!, "dev-term", $"A profile named '{name}' already exists.", ["Replace", "Rename", "Skip"]) switch
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

        // The save-style counterpart to Browse: a real Terminal.Gui SaveDialog, which - unlike
        // OpenDialog - lets you type a brand-new filename that doesn't exist yet, for Export
        // specifically (confirmed via a headless probe against the installed v2.5.0 package that
        // SaveDialog.FileName, not .Path, holds the accepted full path once its own "Save" button
        // is clicked). Browse/OpenDialog stays as the picker for Import (an existing file only).
        saveAsButton.Accepting += (_, e) =>
        {
            var dialog = new SaveDialog { Path = pathField.Text };
            Application.Run(dialog);
            if (!dialog.Canceled && dialog.FileName is { Length: > 0 } fileName)
            {
                pathField.Text = fileName;
            }

            e.Handled = true;
        };

        // A small nested modal picker for "type it yourself, or pick from what's actually attached"
        // - the same Application.Run(dialog)/read-result-after pattern as OpenDialog above, built
        // from a plain Dialog+ListView instead of a Terminal.Gui built-in since there's no built-in
        // combobox widget (checked via reflection against the installed v2.5.0 package - see
        // docs/changes/2026-09-16.md). Selecting a row is wired the same way double-click-to-load
        // is above: ListView's own double-click maps to Command.Accept, raising the inherited
        // Accepting event.
        static string? PickFromList(string title, IReadOnlyList<string> items, string emptyMessage = "Nothing was detected.")
        {
            if (items.Count == 0)
            {
                MessageBox.Query(Application.Instance!, "dev-term", emptyMessage, ["OK"]);
                return null;
            }

            string? picked = null;
            var dialog = new Dialog { Title = title, Width = 60, Height = Math.Min(items.Count + 4, 20) };
            var listView = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() - 1 };
            listView.SetSource(new ObservableCollection<string>(items));
            listView.Accepting += (_, e) =>
            {
                if (listView.SelectedItem is int index && index >= 0 && index < items.Count)
                {
                    picked = items[index];
                }

                e.Handled = true;
                Application.RequestStop();
            };
            var selectButton = new Button { X = 0, Y = Pos.Bottom(listView), Text = "Select", IsDefault = true };
            selectButton.Accepting += (_, e) =>
            {
                if (listView.SelectedItem is int index && index >= 0 && index < items.Count)
                {
                    picked = items[index];
                }

                e.Handled = true;
                Application.RequestStop();
            };
            var cancelButton = new Button { X = Pos.Right(selectButton) + 1, Y = Pos.Top(selectButton), Text = "Cancel" };
            cancelButton.Accepting += (_, e) =>
            {
                e.Handled = true;
                Application.RequestStop();
            };
            dialog.Add(listView, selectButton, cancelButton);
            Application.Run(dialog);
            return picked;
        }

        detectPortButton.Accepting += (_, e) =>
        {
            var ports = viewModel.SerialPortOptions;
            var choice = PickFromList("Detected serial ports", [.. ports.Select(p => p.Display)]);
            if (choice is not null)
            {
                portField.Text = ports.First(p => p.Display == choice).Name;
            }

            e.Handled = true;
        };

        detectHidButton.Accepting += (_, e) =>
        {
            // The picker is filtered by the Vendor/Product ID fields (non-zero = must match), and
            // the TUI's fields only reach the view model when pushed, so push what's typed first.
            PushFieldsIntoViewModel();
            var devices = viewModel.HidDeviceOptions;
            var choice = PickFromList(
                "Detected HID devices",
                [.. devices.Select(d => d.Display)],
                viewModel.HidDevicesHiddenByFilter
                    ? "No detected HID device matches the Vendor/Product ID entered (0 means any)."
                    : "Nothing was detected.");
            if (choice is not null)
            {
                viewModel.SelectedHidDevice = devices.First(d => d.Display == choice);
                vendorField.Text = viewModel.VendorIdDisplay;
                productField.Text = viewModel.ProductIdDisplay;
            }

            e.Handled = true;
        };

        detectUsbtmcButton.Accepting += (_, e) =>
        {
            // Same filtering/push convention as detectHidButton, but against the USBTMC discovery
            // source - the two device lists come from different places even though they write into
            // the same shared Vendor/Product ID fields.
            PushFieldsIntoViewModel();
            var devices = viewModel.UsbtmcDeviceOptions;
            var choice = PickFromList(
                "Detected USBTMC devices",
                [.. devices.Select(d => d.Display)],
                viewModel.UsbtmcDevicesHiddenByFilter
                    ? "No detected USBTMC device matches the Vendor/Product ID entered (0 means any)."
                    : "Nothing was detected.");
            if (choice is not null)
            {
                viewModel.SelectedUsbtmcDevice = devices.First(d => d.Display == choice);
                vendorField.Text = viewModel.VendorIdDisplay;
                productField.Text = viewModel.ProductIdDisplay;
            }

            e.Handled = true;
        };

        // Reformats the two shared fields immediately when the toggle changes, rather than waiting
        // for the next button press. CheckBox.Activated fires *after* Value has already flipped
        // (confirmed via a headless probe against the installed Terminal.Gui v2.5.0 package -
        // Command.Activate, bound to Space, updates Value before raising Activating/Activated), so
        // the currently-displayed text is pushed through the view model's *old* IdsShowHex
        // first - reinterpreting it in whatever format it's actually showing right now - before
        // IdsShowHex itself is updated to match the checkbox's new state.
        idsShowHexCheckBox.Activated += (_, _) =>
        {
            viewModel.VendorIdDisplay = vendorField.Text;
            viewModel.ProductIdDisplay = productField.Text;
            viewModel.IdsShowHex = idsShowHexCheckBox.Value == CheckState.Checked;
            vendorField.Text = viewModel.VendorIdDisplay;
            productField.Text = viewModel.ProductIdDisplay;
        };

        importButton.Accepting += (_, e) =>
        {
            PushFieldsIntoViewModel();
            viewModel.ImportCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

        replaceAllButton.Accepting += (_, e) =>
        {
            PushFieldsIntoViewModel();
            viewModel.ReplaceAllFromZipCommand.Execute(null);
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

        exportSelectedButton.Accepting += (_, e) =>
        {
            PushFieldsIntoViewModel();
            PushMarkedProfilesIntoViewModel();
            viewModel.ExportSelectedProfilesCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

        exportAllButton.Accepting += (_, e) =>
        {
            PushFieldsIntoViewModel();
            viewModel.ExportAllProfilesCommand.Execute(null);
            PullFieldsFromViewModel();
            e.Handled = true;
        };

        deleteSelectedButton.Accepting += (_, e) =>
        {
            PushMarkedProfilesIntoViewModel();
            viewModel.DeleteSelectedProfilesCommand.Execute(null);
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
            exportSelectedButton, exportAllButton, deleteSelectedButton,
            transportLabel, transportSelector,
            descriptionLabel, descriptionField,
            portLabel, portField, detectPortButton, baudLabel, baudField,
            dataBitsLabel, dataBitsField, parityLabel, paritySelector, stopBitsLabel, stopBitsSelector,
            handshakeLabel, handshakeSelector,
            hostLabel, hostField, tcpPortLabel, tcpPortField, listenCheckBox,
            vendorLabel, vendorField, productLabel, productField, detectHidButton, detectUsbtmcButton, idsShowHexCheckBox,
            loopbackInfoLabel,
            presenterLabel, scpiProfileLabel, scpiProfileField, scpiProfilePickButton,
            parserLabel, parserSelector, lineEndingLabel, lineEndingSelector,
            saveNameLabel, saveNameField, saveButton,
            pathLabel, pathField, browseButton, importButton, exportButton, saveAsButton, replaceAllButton,
            connectButton, quitButton);
        foreach (var presenterCheckBox in presenterCheckBoxes)
        {
            formContent.Add(presenterCheckBox);
        }

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

        // Tabbing (or clicking) onto a control that's scrolled out of view scrolls it into view -
        // without this, focus moved to an off-screen field and the user typed blind. Wired per
        // direct child: a container (an option selector's radio buttons) reports HasFocus when any
        // of its own children does, so the direct child's Frame is the right thing to reveal.
        void RevealInViewport(View child)
        {
            var top = child.Frame.Y;
            var bottom = top + child.Frame.Height;
            var viewport = formContent.Viewport;
            if (top < viewport.Y)
            {
                ScrollBy(top - viewport.Y);
            }
            else if (bottom > viewport.Y + viewport.Height)
            {
                ScrollBy(bottom - (viewport.Y + viewport.Height));
            }
        }

        foreach (var child in formContent.SubViews)
        {
            child.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue)
                {
                    RevealInViewport(child);
                }
            };
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

    public required Button ExportSelectedButton { get; init; }

    public required Button ExportAllButton { get; init; }

    public required Button DeleteSelectedButton { get; init; }

    public required OptionSelector<ConfigureMode.TransportChoice> TransportSelector { get; init; }

    public required TextField DescriptionField { get; init; }

    public required TextField PortField { get; init; }

    public required Button DetectPortButton { get; init; }

    public required TextField BaudField { get; init; }

    public required TextField DataBitsField { get; init; }

    public required OptionSelector<Parity> ParitySelector { get; init; }

    public required OptionSelector<StopBits> StopBitsSelector { get; init; }

    public required OptionSelector<Handshake> HandshakeSelector { get; init; }

    public required TextField HostField { get; init; }

    public required TextField TcpPortField { get; init; }

    public required CheckBox ListenCheckBox { get; init; }

    public required TextField VendorField { get; init; }

    public required TextField ProductField { get; init; }

    public required Button DetectHidButton { get; init; }

    public required Button DetectUsbtmcButton { get; init; }

    public required CheckBox IdsShowHexCheckBox { get; init; }

    /// <summary>Shown only when <see cref="ConfigureMode.TransportChoice.Loopback"/> is selected — the loopback transport takes no configuration.</summary>
    public required Label LoopbackInfoLabel { get; init; }

    /// <summary>One checkbox per presenter, in <see cref="ConnectionEditorViewModel.PresenterChoices"/> order — the multi-select presenter picker.</summary>
    public required IReadOnlyList<CheckBox> PresenterCheckBoxes { get; init; }

    /// <summary>Shown only when the "scpi" presenter is checked — see <see cref="ConnectionEditorViewModel.IsScpiPresenterSelected"/>.</summary>
    public required TextField ScpiProfileField { get; init; }

    public required Button ScpiProfilePickButton { get; init; }

    /// <summary>The send format (parser) — an <see cref="ConfigureMode.PresenterChoice"/> because every presenter that registers can encode input.</summary>
    public required OptionSelector<ConfigureMode.PresenterChoice> ParserSelector { get; init; }

    public required OptionSelector<LineEnding> LineEndingSelector { get; init; }

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
