using DevTerm.Core.Control;
using DevTerm.UiDefinitions;

namespace DevTerm.Test.Utilities;

/// <summary>
/// An in-memory <see cref="IDeviceConfigEditor"/> for the Configure device screens: a text IP, a text baud rate (0 is invalid),
/// a choice and a toggle. Changing the IP drops the connection; every write needs a restart. <see cref="Device"/> is the "device".
/// </summary>
public sealed class FakeDeviceConfigEditor : IDeviceConfigEditor
{
    public Dictionary<string, string> Device { get; } = new() { ["ip"] = "192.168.0.201", ["baud"] = "9600", ["mode"] = "tcp-server", ["dhcp"] = "false" };

    public DeviceConfigTarget? LastTarget { get; private set; }

    public string Id => "fake";

    public string Title => "Fake bridge";

    public UiDefinition BuildDefinition() => new()
    {
        Name = "Fake bridge",
        Sections =
        [
            new UiSection
            {
                Label = "Network",
                Controls =
                [
                    new TextFieldControl { Id = "ip", Label = "IP address" },
                    new TextFieldControl { Id = "baud", Label = "Baud" },
                    new ChoiceControl { Id = "mode", Label = "Work mode", Options = ["tcp-server", "tcp-client", "udp"] },
                    new ToggleControl { Id = "dhcp", Label = "DHCP" },
                ],
            },
        ],
    };

    public Task<IReadOnlyDictionary<string, string>> ReadAsync(DeviceConfigTarget target, CancellationToken cancellationToken)
    {
        LastTarget = target;
        return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Device));
    }

    public IReadOnlyList<DeviceConfigIssue> Validate(IReadOnlyDictionary<string, string> values) =>
        values.GetValueOrDefault("baud") == "0" ? [new DeviceConfigIssue("baud", "must be positive")] : [];

    public bool WillDropConnection(DeviceConfigTarget target, IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> values) =>
        current["ip"] != values["ip"];

    public Task<DeviceConfigWriteResult> WriteAsync(DeviceConfigTarget target, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        foreach (var (key, value) in values)
        {
            Device[key] = value;
        }

        return Task.FromResult(new DeviceConfigWriteResult(new Dictionary<string, string>(Device), RebootRequired: true));
    }
}
