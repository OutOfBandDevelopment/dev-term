using DevTerm.Core.Control;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.De5000;

/// <summary>The De5000 control panel as an <see cref="IDevicePanelContribution"/>.</summary>
public sealed class De5000PanelContribution : IDevicePanelContribution
{
    public string Id => "de5000";

    public string MenuTitle => "_DE-5000 LCR Meter...";

    public string? PresenterName => "de5000";

    // The DE-5000 adapter is a BLE GATT peripheral with no VID/PID, so any BLE connection is offered.
    public bool IsAvailable(string transport, int vendorId, int productId) => string.Equals(transport, "ble", StringComparison.OrdinalIgnoreCase);

    public UiDefinition BuildDefinition() => De5000UiDefinition.Build();

    public IControlSurface CreateSurface(Session session) => new De5000ControlSurface();
}
