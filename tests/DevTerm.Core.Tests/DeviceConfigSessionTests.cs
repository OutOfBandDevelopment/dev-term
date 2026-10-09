using DevTerm.Core.Control;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.Core.Tests;

[TestClass]
[TestCategory(TestCategories.Unit)]
public class DeviceConfigSessionTests
{
    private sealed class FakeEditor : IDeviceConfigEditor
    {
        public Dictionary<string, string> Device { get; } = new() { ["ip"] = "192.168.0.201", ["baud"] = "9600" };
        public bool IgnoreBaud { get; set; }
        public int Writes { get; private set; }
        public string Id => "fake";
        public string Title => "Fake";
        public UiDefinition BuildDefinition() => new() { Name = "Fake" };
        public Task<IReadOnlyDictionary<string, string>> ReadAsync(DeviceConfigTarget target, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Device));
        public IReadOnlyList<DeviceConfigIssue> Validate(IReadOnlyDictionary<string, string> values) =>
            values["baud"] == "0" ? [new DeviceConfigIssue("baud", "must be positive")] : [];
        public bool WillDropConnection(DeviceConfigTarget target, IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> values) =>
            current["ip"] != values["ip"];
        public Task<DeviceConfigWriteResult> WriteAsync(DeviceConfigTarget target, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
        {
            Writes++;
            foreach (var (k, v) in values)
            {
                if (!(IgnoreBaud && k == "baud"))
                {
                    Device[k] = v;
                }
            }

            return Task.FromResult(new DeviceConfigWriteResult(new Dictionary<string, string>(Device), RebootRequired: true));
        }
    }

    [TestMethod]
    public async Task EditThenWrite_ReportsTheChangesFirst_AndConfirmsViaReadBack()
    {
        var editor = new FakeEditor();
        var session = new DeviceConfigSession(editor, new DeviceConfigTarget("192.168.0.201", 1500));
        await session.ReadAsync();

        session.Set("baud", "115200");
        var change = session.Changes().Single();
        Assert.AreEqual("baud", change.FieldId);
        Assert.AreEqual("9600", change.From);
        Assert.AreEqual("115200", change.To);
        Assert.IsFalse(session.WillDropConnection());
        Assert.AreEqual(0, editor.Writes, "Nothing is written until WriteAsync.");

        var mismatches = await session.WriteAsync();
        Assert.IsEmpty(mismatches);
        Assert.IsTrue(session.RebootRequired);
        Assert.IsEmpty(session.Changes());
    }

    [TestMethod]
    public async Task ChangingTheIp_WarnsThatTheConnectionWillDrop()
    {
        var session = new DeviceConfigSession(new FakeEditor(), new DeviceConfigTarget("192.168.0.201"));
        await session.ReadAsync();
        session.Set("ip", "192.168.0.50");
        Assert.IsTrue(session.WillDropConnection());
    }

    [TestMethod]
    public async Task ReadBackThatDiffers_IsReturnedAsAMismatch()
    {
        var editor = new FakeEditor { IgnoreBaud = true };
        var session = new DeviceConfigSession(editor, new DeviceConfigTarget("h"));
        await session.ReadAsync();
        session.Set("baud", "115200");

        var mismatches = await session.WriteAsync();
        Assert.AreEqual("baud", mismatches.Single().FieldId);
    }

    [TestMethod]
    public async Task Write_IsRefusedBeforeARead_WhenInvalid_AndSkippedWhenNothingChanged()
    {
        var editor = new FakeEditor();
        var session = new DeviceConfigSession(editor, new DeviceConfigTarget("h"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.WriteAsync());

        await session.ReadAsync();
        Assert.IsEmpty(await session.WriteAsync());
        Assert.AreEqual(0, editor.Writes);

        session.Set("baud", "0");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.WriteAsync());
    }
}
