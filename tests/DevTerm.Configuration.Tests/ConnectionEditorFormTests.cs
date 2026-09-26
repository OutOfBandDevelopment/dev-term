using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;
using DevTerm.UiDefinitions.Forms;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// The Connection Editor's connection fields as a generated form: <see cref="ConnectionEditorViewModel.FormDefinition"/>
/// (from the view model's own annotations) and, bound through <see cref="FormBinding"/>, which
/// transport's fields it shows — the front-end-agnostic half of what <c>ConfigureMode</c> and
/// <c>DeviceProfilesWindow</c> now render. Also <see cref="CliOptions"/>, the unannotated model the
/// generator started from.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ConnectionEditorFormTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "devterm-editor-form-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WithViewModel(CliOptions initial, Action<ConnectionEditorViewModel> body)
    {
        var directory = CreateTempDirectory();
        try
        {
            using var viewModel = new ConnectionEditorViewModel(new ConnectionProfileStore(directory), initial);
            body(viewModel);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<UiControl> Controls(UiDefinition definition) => definition.Sections.SelectMany(s => s.Controls);

    [TestMethod]
    public void FormDefinition_GroupsTheFieldsLikeTheEditor_WithOneSectionPerTransport()
    {
        WithViewModel(new CliOptions(), viewModel =>
        {
            var definition = viewModel.FormDefinition;

            Assert.AreSequenceEqual(["", "Serial", "TCP", "USB Device", "BLE", "Loopback", "Presentation"], [.. definition.Sections.Select(s => s.Label)]);
            Assert.AreSequenceEqual(
                [null, nameof(ConnectionEditorViewModel.IsSerialTransport), nameof(ConnectionEditorViewModel.IsTcpTransport), nameof(ConnectionEditorViewModel.IsUsbDeviceTransport), nameof(ConnectionEditorViewModel.IsBleTransport), nameof(ConnectionEditorViewModel.IsLoopbackTransport), null],
                [.. definition.Sections.Select(s => s.VisibleWhen?.Id)]);
            Assert.AreSequenceEqual(["Transport", "Description"], [.. definition.Sections[0].Controls.Select(c => c.Label)]);
            Assert.AreSequenceEqual(
                ["Port", "Detected ports", "", "Baud", "Data bits", "Parity", "Stop bits", "Handshake"],
                [.. definition.Sections[1].Controls.Select(c => c.Label)]);
            Assert.AreSequenceEqual(["Host", "Port", "Listen (server mode)"], [.. definition.Sections[2].Controls.Select(c => c.Label)]);
            Assert.AreSequenceEqual(
                ["Vendor ID", "Product ID", "Serial number", "Show as hex", "Detected HID devices", "Detected USBTMC devices", ""],
                [.. definition.Sections[3].Controls.Select(c => c.Label)]);
            Assert.AreSequenceEqual(["Presenters", "SCPI profile", "Send as", "Line ending"], [.. definition.Sections[6].Controls.Select(c => c.Label)]);

            var transport = (ChoiceControl)definition.Sections[0].Controls[0];
            Assert.AreSequenceEqual(viewModel.TransportOptions, transport.Options);
            var presenters = (ChoiceControl)definition.Sections[6].Controls[0];
            Assert.AreEqual(ChoiceStyle.CheckList, presenters.Style);
            Assert.AreSequenceEqual(viewModel.PresenterOptions, presenters.Options);
            Assert.AreEqual(ValueKind.Integer, ((TextFieldControl)definition.Sections[1].Controls[3]).Constraint!.Kind);
            Assert.AreEqual(IndicatorStyle.Warning, ((IndicatorControl)definition.Sections[1].Controls[2]).Style);
            Assert.AreEqual(nameof(ConnectionEditorViewModel.IsScpiPresenterSelected), definition.Sections[6].Controls[1].VisibleWhen!.Id);
            Assert.AreEqual(nameof(ConnectionEditorViewModel.IsHidTransport), definition.Sections[3].Controls[4].VisibleWhen!.Id);
        });
    }

    [TestMethod]
    [DataRow("serial", "Serial")]
    [DataRow("tcp", "TCP")]
    [DataRow("hid", "USB Device")]
    [DataRow("usbtmc", "USB Device")]
    [DataRow("ble", "BLE")]
    [DataRow("loopback", "Loopback")]
    public void EachTransport_ShowsExactlyItsOwnFieldGroup(string transport, string expected)
    {
        WithViewModel(new CliOptions(), viewModel =>
        {
            using var binding = new FormBinding(viewModel);
            viewModel.Transport = transport;

            var shown = viewModel.FormDefinition.Sections.Where(s => s.VisibleWhen is not null && binding.IsVisible(s.VisibleWhen)).Select(s => s.Label).ToList();

            Assert.AreSequenceEqual([expected], shown);
        });
    }

    [TestMethod]
    public void PresentersText_IsTheCheckListsValue_AndEditingItIsAnEdit()
    {
        WithViewModel(new CliOptions { Presenter = ["ascii", "binary"] }, viewModel =>
        {
            Assert.AreEqual("ascii,binary", viewModel.PresentersText);
            Assert.IsFalse(viewModel.IsDirty);

            using var binding = new FormBinding(viewModel);
            var changes = new List<string>();
            binding.Changed += (_, name) => changes.Add(name ?? string.Empty);
            binding.SetSelection(nameof(ConnectionEditorViewModel.PresentersText), ["hex", "scpi"]);

            Assert.AreSequenceEqual(["hex", "scpi"], [.. viewModel.SelectedPresenters]);
            Assert.IsTrue(viewModel.IsScpiPresenterSelected, "Which reveals the SCPI profile row.");
            Assert.IsTrue(viewModel.IsDirty);
            Assert.Contains(nameof(ConnectionEditorViewModel.PresentersText), changes);
        });
    }

    [TestMethod]
    public void NotFoundHints_AreShownOnlyWhileTheSavedDeviceIsMissing()
    {
        WithViewModel(new CliOptions { Transport = "serial", Port = "COM_DEVTERM_NOPE" }, viewModel =>
        {
            using var binding = new FormBinding(viewModel);
            var hint = Controls(viewModel.FormDefinition).Single(c => c.Id == nameof(ConnectionEditorViewModel.SerialPortNotFoundHint));

            Assert.IsTrue(binding.IsVisible(hint.VisibleWhen));
            Assert.StartsWith("(not found", binding.GetText(hint.Id));

            viewModel.Port = string.Empty;
            Assert.IsFalse(binding.IsVisible(hint.VisibleWhen));
        });
    }

    [TestMethod]
    public void CliOptions_Unannotated_GeneratesAFieldPerBrowsableSetting()
    {
        var definition = FormDefinitionGenerator.Generate<CliOptions>();

        Assert.AreSequenceEqual(["General", "Mode", "Presentation", "Serial", "TCP", "USB Device", "BLE"], [.. definition.Sections.Select(s => s.Label)]);
        var ids = Controls(definition).Select(c => c.Id).ToList();
        Assert.DoesNotContain(nameof(CliOptions.EffectivePresenters), ids, "[Browsable(false)] is left out.");
        Assert.Contains(nameof(CliOptions.Baud), ids);

        var baud = (TextFieldControl)Controls(definition).Single(c => c.Id == nameof(CliOptions.Baud));
        Assert.AreEqual("9600", baud.DefaultValue);
        Assert.AreEqual(ValueKind.Integer, baud.Constraint!.Kind);
        var parity = (ChoiceControl)Controls(definition).Single(c => c.Id == nameof(CliOptions.Parity));
        Assert.Contains("Even", parity.Options);
        Assert.AreEqual("Read timeout (ms)", Controls(definition).Single(c => c.Id == nameof(CliOptions.ReadTimeoutMs)).Label);
        Assert.IsTrue(((ToggleControl)Controls(definition).Single(c => c.Id == nameof(CliOptions.Dtr))).DefaultValue);
        Assert.AreEqual(
            "Which transport to use: serial, tcp, hid, usbtmc, ble, or loopback.",
            Controls(definition).Single(c => c.Id == nameof(CliOptions.Transport)).Description);
    }
}
