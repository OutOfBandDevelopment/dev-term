using DevTerm.Core.Control;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.K8055;

/// <summary>The K8055 control panel as an <see cref="IDevicePanelContribution"/>: what the web host (and any plugin-style front end) resolves by id.</summary>
public sealed class K8055PanelContribution : IDevicePanelContribution
{
    public string Id => "k8055";

    public string MenuTitle => "_K8055 Control Panel...";

    public string? PresenterName => "k8055";

    // Velleman K8055/VM110: vendor 0x10CF, one product id per board address jumper setting (0-3).
    public bool IsAvailable(string transport, int vendorId, int productId) =>
        string.Equals(transport, "hid", StringComparison.OrdinalIgnoreCase) && vendorId == 0x10CF && productId is >= 0x5500 and <= 0x5503;

    public UiDefinition BuildDefinition() => K8055UiDefinition.Build();

    public IControlSurface CreateSurface(Session session) => new K8055ControlSurface(session);
}
