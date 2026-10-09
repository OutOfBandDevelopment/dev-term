using System.Linq;
using System.Windows.Controls;
using DevTerm.Configuration;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>The WPF Configure device window over a fake editor (no real editor is registered yet). Never shown, like the other window tests.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class ConfigureDeviceWindowTests
{
    [TestMethod]
    public void BeforeRead_HasNoFields_AndWriteIsDisabled()
    {
        StaTestRunner.Run(() =>
        {
            var window = new ConfigureDeviceWindow(new DeviceConfigViewModel([new FakeDeviceConfigEditor()], "10.0.0.5", "8899"));
            StaTestRunner.DoEvents();

            Assert.IsEmpty(window.Form.Children);
            Assert.IsFalse(window.CanWrite);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Read_FillsTheForm_AndEditingThenWriting_ReachesTheDevice()
    {
        var editor = new FakeDeviceConfigEditor();
        StaTestRunner.Run(async () =>
        {
            var window = new ConfigureDeviceWindow(new DeviceConfigViewModel([editor], "10.0.0.5", "8899"));
            StaTestRunner.DoEvents();

            await window.ReadNowAsync();
            StaTestRunner.DoEvents();

            var texts = window.Form.Children.OfType<TextBox>().ToList();
            Assert.HasCount(2, texts);
            Assert.AreEqual("192.168.0.201", texts[0].Text);
            Assert.IsNotEmpty(window.Form.Children.OfType<CheckBox>());
            Assert.IsNotEmpty(window.Form.Children.OfType<ComboBox>());

            texts[1].Text = "19200";
            Assert.IsTrue(window.CanWrite);

            await window.WriteNowAsync();

            Assert.AreEqual("19200", editor.Device["baud"]);
            StringAssert.StartsWith(window.Status, "Written; the device confirmed every value.");
            Assert.IsFalse(window.CanWrite);
        });
    }

    [TestMethod]
    public void InvalidValue_DisablesWrite()
    {
        StaTestRunner.Run(async () =>
        {
            var window = new ConfigureDeviceWindow(new DeviceConfigViewModel([new FakeDeviceConfigEditor()], "h", "1"));
            await window.ReadNowAsync();

            window.Form.Children.OfType<TextBox>().ToList()[1].Text = "0";

            Assert.IsFalse(window.CanWrite);
        });
    }
}
