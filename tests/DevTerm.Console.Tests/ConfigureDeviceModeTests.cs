using DevTerm.Configuration;
using DevTerm.Test.Utilities;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>The TUI's Configure device dialog, driven headlessly over a fake editor (no real editor is registered yet).</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ConfigureDeviceModeTests
{
    private static void Run(FakeDeviceConfigEditor editor, Action<ConfigureDeviceParts, DeviceConfigViewModel> body) =>
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var viewModel = new DeviceConfigViewModel([editor], "10.0.0.5", "8899");
            var parts = ConfigureDeviceMode.BuildWindow(app, viewModel);
            var token = app.Begin(parts.Dialog) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                body(parts, viewModel);
            }
            finally
            {
                app.End(token);
            }
        });

    [TestMethod]
    public void BeforeRead_ShowsNoFieldsAndWriteIsDisabled()
    {
        Run(new FakeDeviceConfigEditor(), (parts, _) =>
        {
            Assert.AreEqual("10.0.0.5", parts.HostField.Text);
            Assert.AreEqual("8899", parts.PortField.Text);
            Assert.IsEmpty(parts.Form.SubViews);
            Assert.IsFalse(parts.WriteButton.Enabled);
        });
    }

    [TestMethod]
    public void Read_FillsTheForm_AndEditingThenWriting_ReachesTheDevice()
    {
        var editor = new FakeDeviceConfigEditor();
        Run(editor, (parts, viewModel) =>
        {
            parts.Read();

            Assert.AreEqual(new DevTerm.Core.Control.DeviceConfigTarget("10.0.0.5", 8899), editor.LastTarget);
            var fields = parts.Form.SubViews.OfType<TextField>().ToList();
            Assert.HasCount(2, fields);
            Assert.AreEqual("192.168.0.201", fields[0].Text);
            Assert.IsNotEmpty(parts.Form.SubViews.OfType<CheckBox>());
            Assert.IsNotEmpty(parts.Form.SubViews.OfType<OptionSelector>());

            fields[1].Text = "19200";
            Assert.AreEqual("19200", viewModel.Value("baud"));
            Assert.IsTrue(parts.WriteButton.Enabled);

            parts.Write();

            Assert.AreEqual("19200", editor.Device["baud"]);
            StringAssert.StartsWith(parts.Status.Text, "Written; the device confirmed every value.");
            Assert.IsFalse(parts.WriteButton.Enabled, "Nothing left to write.");
        });
    }

    [TestMethod]
    public void InvalidValue_DisablesWrite()
    {
        Run(new FakeDeviceConfigEditor(), (parts, _) =>
        {
            parts.Read();
            parts.Form.SubViews.OfType<TextField>().ToList()[1].Text = "0";
            Assert.IsFalse(parts.WriteButton.Enabled);
        });
    }
}
