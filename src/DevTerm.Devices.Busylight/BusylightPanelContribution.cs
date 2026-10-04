using DevTerm.Core.Control;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.Busylight;

/// <summary>The Busylight control panel as an <see cref="IDevicePanelContribution"/>: what the web host (and any plugin-style front end) resolves by id.</summary>
public sealed class BusylightPanelContribution : IDevicePanelContribution
{
    public string Id => "busylight";

    public string MenuTitle => "_Busylight Control Panel...";

    public string? PresenterName => "busylight";

    // The Microchip-vendor bench unit (0x04D8:0xF848) plus Plenom's own vendor id (0x27BB), as DevicePanels gates the built-in item.
    public bool IsAvailable(string transport, int vendorId, int productId) =>
        string.Equals(transport, "hid", StringComparison.OrdinalIgnoreCase)
        && ((vendorId == 0x04D8 && productId == 0xF848) || vendorId == 0x27BB);

    public UiDefinition BuildDefinition() => BusylightUiDefinition.Build();

    public IControlSurface CreateSurface(Session session) => new BusylightControlSurface(session);
}
