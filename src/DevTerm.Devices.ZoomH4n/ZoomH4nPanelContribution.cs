using DevTerm.Core.Control;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.ZoomH4n;

/// <summary>The ZoomH4n control panel as an <see cref="IDevicePanelContribution"/>.</summary>
public sealed class ZoomH4nPanelContribution : IDevicePanelContribution
{
    public string Id => "zoomh4n";

    public string MenuTitle => "_Zoom H4n Remote...";

    public string? PresenterName => "zoomh4n";

    // The H4n remote port is presented as plain serial (no VID/PID), so any serial connection is offered.
    public bool IsAvailable(string transport, int vendorId, int productId) => string.Equals(transport, "serial", StringComparison.OrdinalIgnoreCase);

    public UiDefinition BuildDefinition() => ZoomH4nUiDefinition.Build();

    public IControlSurface CreateSurface(Session session) => new ZoomH4nControlSurface(session);
}
