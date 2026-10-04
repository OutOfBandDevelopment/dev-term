using DevTerm.Core.Control;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.RadexOne;

/// <summary>The RadexOne control panel as an <see cref="IDevicePanelContribution"/>.</summary>
public sealed class RadexOnePanelContribution : IDevicePanelContribution
{
    public string Id => "radexone";

    public string MenuTitle => "_Radex One Control Panel...";

    public string? PresenterName => "radexone";

    // Radex One is a plain virtual-COM-port device (2400 8N1), so any serial connection is offered.
    public bool IsAvailable(string transport, int vendorId, int productId) => string.Equals(transport, "serial", StringComparison.OrdinalIgnoreCase);

    public UiDefinition BuildDefinition() => RadexOneUiDefinition.Build();

    public IControlSurface CreateSurface(Session session) => new RadexOneControlSurface(session);
}
