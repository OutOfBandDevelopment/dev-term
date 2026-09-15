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
/// hand, and optionally save the result as a new named profile. See
/// docs/design/connection-profiles.md.
/// </summary>
/// <remarks>
/// A first stub, not the full design: all fields are always visible rather than shown/hidden per
/// selected transport, and there's no live device-manifest picker yet (a manifest is still
/// referenced by typing its name, matching <c>--manifestname</c>). See
/// docs/design/frontends.md's startup/configure flow note for the target.
/// </remarks>
public static class ConfigureMode
{
    private static readonly CliOptionsValidator Validator = new();

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
        profilesList.SetSource(new ObservableCollection<string>(profileStore.List()));

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

        var connectButton = new Button { X = 0, Y = Pos.Bottom(saveNameLabel) + 1, Text = "Connect", IsDefault = true };
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
            ConnectButton = connectButton,
            QuitButton = quitButton,
        };

        CliOptions BuildOptionsFromFields()
        {
            var options = new CliOptions
            {
                Transport = transportField.Text.Trim(),
                Port = portField.Text.Trim() is { Length: > 0 } p ? p : null,
                Host = hostField.Text.Trim() is { Length: > 0 } h ? h : null,
                Listen = listenCheckBox.Value == CheckState.Checked,
                Presenter = presenterField.Text.Trim() is { Length: > 0 } pr ? pr : "hex",
            };

            if (int.TryParse(baudField.Text, out var baud))
            {
                options.Baud = baud;
            }

            if (int.TryParse(tcpPortField.Text, out var tcpPort))
            {
                options.TcpPort = tcpPort;
            }

            if (int.TryParse(hidVendorField.Text, out var vendorId))
            {
                options.HidVendorId = vendorId;
            }

            if (int.TryParse(hidProductField.Text, out var productId))
            {
                options.HidProductId = productId;
            }

            if (Enum.TryParse<LineEnding>(lineEndingField.Text, ignoreCase: true, out var lineEnding))
            {
                options.LineEnding = lineEnding;
            }

            return options;
        }

        void LoadIntoFields(CliOptions options)
        {
            transportField.Text = options.Transport;
            portField.Text = options.Port ?? string.Empty;
            baudField.Text = options.Baud.ToString();
            hostField.Text = options.Host ?? string.Empty;
            tcpPortField.Text = options.TcpPort.ToString();
            listenCheckBox.Value = options.Listen ? CheckState.Checked : CheckState.UnChecked;
            hidVendorField.Text = options.HidVendorId.ToString();
            hidProductField.Text = options.HidProductId.ToString();
            presenterField.Text = options.Presenter;
            lineEndingField.Text = options.LineEnding.ToString();
        }

        loadButton.Accepting += (_, e) =>
        {
            if (profilesList.Source is null || profilesList.SelectedItem is not int index || index < 0)
            {
                errorLabel.Text = "Select a profile first.";
                return;
            }

            var name = profileStore.List()[index];
            try
            {
                LoadIntoFields(profileStore.Load(name));
                errorLabel.Text = $"Loaded profile '{name}'.";
            }
            catch (Exception ex)
            {
                errorLabel.Text = $"Could not load profile '{name}': {ex.Message}";
            }

            e.Handled = true;
        };

        saveButton.Accepting += (_, e) =>
        {
            var name = saveNameField.Text.Trim();
            if (name.Length == 0)
            {
                errorLabel.Text = "Type a name to save this connection as a profile.";
                e.Handled = true;
                return;
            }

            profileStore.Save(name, BuildOptionsFromFields());
            profilesList.SetSource(new ObservableCollection<string>(profileStore.List()));
            errorLabel.Text = $"Saved profile '{name}'.";
            e.Handled = true;
        };

        connectButton.Accepting += (_, e) =>
        {
            var options = BuildOptionsFromFields();
            var validation = Validator.Validate(null, options);
            if (validation.Failed)
            {
                errorLabel.Text = string.Join(" ", validation.Failures);
                e.Handled = true;
                return;
            }

            parts.Result = options;
            e.Handled = true;
            Application.RequestStop();
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

    public required Button ConnectButton { get; init; }

    public required Button QuitButton { get; init; }

    /// <summary>Set once the user presses Connect (to the validated options) or Quit (to <see langword="null"/>); <see langword="null"/> until either happens.</summary>
    public CliOptions? Result { get; set; }
}
