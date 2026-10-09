using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

/// <summary>The shared Configure device flow the TUI and WPF screens render.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class DeviceConfigViewModelTests
{
    [TestMethod]
    public void OneEditor_IsPreselected_ManyAreNot()
    {
        Assert.AreEqual("fake", new DeviceConfigViewModel([new FakeDeviceConfigEditor()]).Editor?.Id);
        Assert.IsNull(new DeviceConfigViewModel([new FakeDeviceConfigEditor(), new FakeDeviceConfigEditor()]).Editor);
    }

    [TestMethod]
    public async Task Read_ThenEdit_ListsChanges_AndWriteConfirmsTheReadBack()
    {
        var editor = new FakeDeviceConfigEditor();
        var vm = new DeviceConfigViewModel([editor], "10.0.0.5", "8899");

        Assert.IsTrue(vm.CanRead);
        Assert.IsFalse(vm.CanWrite, "Nothing is read yet.");
        await vm.ReadAsync();

        Assert.AreEqual(new DevTerm.Core.Control.DeviceConfigTarget("10.0.0.5", 8899), editor.LastTarget);
        Assert.AreEqual("192.168.0.201", vm.Value("ip"));
        Assert.IsFalse(vm.IsOn("dhcp"));
        CollectionAssert.AreEqual(new[] { "ip", "baud", "mode", "dhcp" }, vm.Fields.Select(f => f.Id).ToArray());
        Assert.IsFalse(vm.CanWrite, "No change yet.");

        vm.Set("baud", "19200");
        vm.SetOn("dhcp", true);
        CollectionAssert.AreEqual(new[] { "baud: 9600 to 19200", "dhcp: false to true" }, vm.ChangeLines().ToArray());
        Assert.IsTrue(vm.CanWrite);
        Assert.IsFalse(vm.WillDropConnection);

        await vm.WriteAsync();

        StringAssert.StartsWith(vm.Message, "Written; the device confirmed every value.");
        StringAssert.Contains(vm.Message, "Restart the device");
        Assert.AreEqual("19200", editor.Device["baud"]);
        Assert.IsEmpty(vm.ChangeLines());
    }

    [TestMethod]
    public async Task InvalidValue_ShowsAnIssue_AndBlocksWriting()
    {
        var vm = new DeviceConfigViewModel([new FakeDeviceConfigEditor()], "h", "1");
        await vm.ReadAsync();

        vm.Set("baud", "0");

        CollectionAssert.AreEqual(new[] { "baud: must be positive" }, vm.IssueLines().ToArray());
        Assert.IsFalse(vm.CanWrite);
    }

    [TestMethod]
    public async Task ChangingTheIp_WarnsTheConnectionWillDrop()
    {
        var vm = new DeviceConfigViewModel([new FakeDeviceConfigEditor()], "h", "1");
        await vm.ReadAsync();

        vm.Set("ip", "192.168.0.250");

        Assert.IsTrue(vm.WillDropConnection);
    }

    [TestMethod]
    public async Task ReadWithoutAnEditor_ExplainsInsteadOfThrowing_AndPickingClearsTheSession()
    {
        var vm = new DeviceConfigViewModel([new FakeDeviceConfigEditor(), new FakeDeviceConfigEditor()], "h", "1");
        Assert.IsFalse(vm.CanRead);
        await vm.ReadAsync();
        Assert.AreEqual("Choose an editor first.", vm.Message);

        vm.PickEditor("fake");
        await vm.ReadAsync();
        Assert.IsNotNull(vm.Session);
        vm.PickEditor("fake");
        Assert.IsNull(vm.Session);
        Assert.IsNull(vm.Message);
    }
}
