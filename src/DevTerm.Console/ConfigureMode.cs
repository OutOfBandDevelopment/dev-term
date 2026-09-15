using System.Collections.ObjectModel;
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
/// A first stub, not the full design: all fields are always visible rather than shown/hidden per
/// selected transport, and there's no live device-manifest picker yet (a manifest is still
/// referenced by typing its name, matching <c>--manifestname</c>). See
/// docs/design/frontends.md's startup/configure flow note for the target.
///
/// All connect/load/save/import/export logic lives in the shared <see cref="ConnectionEditorViewModel"/>
/// (also used by WPF's <c>DeviceProfilesWindow</c> via XAML command bindings) — this class only
/// builds Terminal.Gui controls and copies values to/from the view model around each button press,
/// since Terminal.Gui has no data-binding system of its own to do that automatically the way WPF's
/// <c>{Binding ...}</c> does.
/// </remarks>
public static class ConfigureMode
{
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

        var transportLabel = new Label { X = 0, Y = Pos.Bottom(profilesList) + 1, Text = "Transport (serial/tcp/hid):" };
        var transportField = new TextField { X = Pos.Right(transportLabel) + 1, Y = Pos.Top(transportLabel), Width = 12, Text = initial.Transport };

        var portLabel = new Label { X = 0, Y = Pos.Bottom(transportLabel) + 1, Text = "Serial port:" };
        var portField = new TextField { X = Pos.Right(portLabel) + 1, Y = Pos.Top(portLabel), Width = 12, Text = initial.Port ?? string.Empty };
        var baudLabel = new Label { X = Pos.Right(portField) + 3, Y = Pos.Top(portLabel), Text = "Baud:" };
        var baudField = new TextField { X = Pos.Right(baudLabel) + 1, Y = Pos.Top(portLabel), Width = 10, Text = initial.Baud.ToString() };

        var hostLabel = new Label { X = 0, Y = Pos.Bottom(portLabel) + 1, Text = "TCP host:" };
        var hostField = new TextField { X = Pos.Right(hostLabel) + 1, Y = Pos.Top(hostLabel), Width = 20, Text = initial.Host ?? string.Empty };
        var tcpPortLabel = new Label { X = Pos.Right(hostField) + 3, Y = Pos.Top(hostLabel), Text = "Port:" };
        var tcpPortField = new TextField { X = Pos.Right(tcpPortLabel) + 1, Y = Pos.Top(hostLabel), Width = 8, Text = initial.TcpPort.ToString() };
        var listenCheckBox = new CheckBox { X = Pos.Right(tcpPortField) + 3, Y = Pos.Top(hostLabel), Text = "Listen", Value = initial.Listen ? CheckState.Checked : CheckState.UnChecked };

        var hidVendorLabel = new Label { X = 0, Y = Pos.Bottom(hostLabel) + 1, Text = "HID vendor ID (decimal):" };
        var hidVendorField = new TextField { X = Pos.Right(hidVendorLabel) + 1, Y = Pos.Top(hidVendorLabel), Width = 10, Text = initial.HidVendorId.ToString() };
        var hidProductLabel = new Label { X = Pos.Right(hidVendorField) + 3, Y = Pos.Top(hidVendorLabel), Text = "Product ID:" };
        var hidProductField = new TextField { X = Pos.Right(hidProductLabel) + 1, Y = Pos.Top(hidVendorLabel), Width = 10, Text = initial.HidProductId.ToString() };

        var presenterLabel = new Label { X = 0, Y = Pos.Bottom(hidVendorLabel) + 1, Text = "Presenter:" };
        var presenterField = new TextField { X = Pos.Right(presenterLabel) + 1, Y = Pos.Top(presenterLabel), Width = 12, Text = initial.Presenter };
        var lineEndingLabel = new Label { X = Pos.Right(presenterField) + 3, Y = Pos.Top(presenterLabel), Text = "Line ending (None/Cr/Lf/CrLf):" };
        var lineEndingField = new TextField { X = Pos.Right(lineEndingLabel) + 1, Y = Pos.Top(presenterLabel), Width = 8, Text = initial.LineEnding.ToString() };

        var saveNameLabel = new Label { X = 0, Y = Pos.Bottom(presenterLabel) + 1, Text = "Save as profile named:" };
        var saveNameField = new TextField { X = Pos.Right(saveNameLabel) + 1, Y = Pos.Top(saveNameLabel), Width = 20 };
        var saveButton = new Button { X = Pos.Right(saveNameField) + 1, Y = Pos.Top(saveNameLabel), Text = "Save Profile" };

        var pathLabel = new Label { X = 0, Y = Pos.Bottom(saveNameLabel) + 1, Text = "Import/export file path:" };
        var pathField = new TextField { X = Pos.Right(pathLabel) + 1, Y = Pos.Top(pathLabel), Width = 30 };
        var importButton = new Button { X = Pos.Right(pathField) + 1, Y = Pos.Top(pathLabel), Text = "Import" };
        var exportButton = new Button { X = Pos.Right(importButton) + 1, Y = Pos.Top(pathLabel), Text = "Export" };

        var connectButton = new Button { X = 0, Y = Pos.Bottom(pathLabel) + 1, Text = "Connect", IsDefault = true };
        var quitButton = new Button { X = Pos.Right(connectButton) + 2, Y = Pos.Top(connectButton), Text = "Quit" };

        var parts = new ConfigureWindowParts
        {
            Window = window,
            ErrorLabel = errorLabel,
            ProfilesList = profilesList,
            LoadButton = loadButton,
            TransportField = transportField,
            PortField = portField,
            BaudField = baudField,
            HostField = hostField,
            TcpPortField = tcpPortField,
            ListenCheckBox = listenCheckBox,
            HidVendorField = hidVendorField,
            HidProductField = hidProductField,
            PresenterField = presenterField,
            LineEndingField = lineEndingField,
            SaveNameField = saveNameField,
            SaveButton = saveButton,
            PathField = pathField,
            ImportButton = importButton,
            ExportButton = exportButton,
            ConnectButton = connectButton,
            QuitButton = quitButton,
        };

        // Terminal.Gui has no data-binding system, so fields are copied to/from the shared view
        // model explicitly around each button press, rather than staying continuously in sync the
        // way WPF's {Binding ...} does for DeviceProfilesWindow.
        void PushFieldsIntoViewModel()
        {
            viewModel.Transport = transportField.Text;
            viewModel.Port = portField.Text;
            viewModel.Baud = baudField.Text;
            viewModel.Host = hostField.Text;
            viewModel.TcpPort = tcpPortField.Text;
            viewModel.Listen = listenCheckBox.Value == CheckState.Checked;
            viewModel.HidVendorId = hidVendorField.Text;
            viewModel.HidProductId = hidProductField.Text;
            viewModel.Presenter = presenterField.Text;
            viewModel.LineEndingText = lineEndingField.Text;
            viewModel.SaveName = saveNameField.Text;
            viewModel.ImportExportPath = pathField.Text;
        }

        void PullFieldsFromViewModel()
        {
            transportField.Text = viewModel.Transport;
            portField.Text = viewModel.Port;
            baudField.Text = viewModel.Baud;
            hostField.Text = viewModel.Host;
            tcpPortField.Text = viewModel.TcpPort;
            listenCheckBox.Value = viewModel.Listen ? CheckState.Checked : CheckState.UnChecked;
            hidVendorField.Text = viewModel.HidVendorId;
            hidProductField.Text = viewModel.HidProductId;
            presenterField.Text = viewModel.Presenter;
            lineEndingField.Text = viewModel.LineEndingText;
            saveNameField.Text = viewModel.SaveName;
            errorLabel.Text = viewModel.StatusMessage;
            profilesList.SetSource(new ObservableCollection<string>(viewModel.Profiles));
        }

        viewModel.CloseRequested += (_, _) =>
        {
            parts.Result = viewModel.Result;
            Application.RequestStop();
        };

        loadButton.Accepting += (_, e) =>
        {
            if (profilesList.Source is null || profilesList.SelectedItem is not int index || index < 0 || index >= viewModel.Profiles.Count)
            {
                errorLabel.Text = "Select a profile first.";
                e.Handled = true;
                return;
            }

            viewModel.SelectedProfileName = viewModel.Profiles[index];
            viewModel.LoadCommand.Execute(null);
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
            parts.Result = null;
            e.Handled = true;
            Application.RequestStop();
        };

        window.Add(
            errorLabel, profilesLabel, profilesList, loadButton,
            transportLabel, transportField,
            portLabel, portField, baudLabel, baudField,
            hostLabel, hostField, tcpPortLabel, tcpPortField, listenCheckBox,
            hidVendorLabel, hidVendorField, hidProductLabel, hidProductField,
            presenterLabel, presenterField, lineEndingLabel, lineEndingField,
            saveNameLabel, saveNameField, saveButton,
            pathLabel, pathField, importButton, exportButton,
            connectButton, quitButton);

        return parts;
    }
}

/// <summary>The controls a test needs to drive <see cref="ConfigureMode"/> headlessly.</summary>
internal sealed class ConfigureWindowParts
{
    public required Window Window { get; init; }

    public required Label ErrorLabel { get; init; }

    public required ListView ProfilesList { get; init; }

    public required Button LoadButton { get; init; }

    public required TextField TransportField { get; init; }

    public required TextField PortField { get; init; }

    public required TextField BaudField { get; init; }

    public required TextField HostField { get; init; }

    public required TextField TcpPortField { get; init; }

    public required CheckBox ListenCheckBox { get; init; }

    public required TextField HidVendorField { get; init; }

    public required TextField HidProductField { get; init; }

    public required TextField PresenterField { get; init; }

    public required TextField LineEndingField { get; init; }

    public required TextField SaveNameField { get; init; }

    public required Button SaveButton { get; init; }

    public required TextField PathField { get; init; }

    public required Button ImportButton { get; init; }

    public required Button ExportButton { get; init; }

    public required Button ConnectButton { get; init; }

    public required Button QuitButton { get; init; }

    /// <summary>Set once the user presses Connect (to the validated options) or Quit (to <see langword="null"/>); <see langword="null"/> until either happens.</summary>
    public CliOptions? Result { get; set; }
}
