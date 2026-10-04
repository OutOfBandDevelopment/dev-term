using DevTerm.Core.Control;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.Demo;

/// <summary>The demo panel as an <see cref="IDevicePanelContribution"/>, available on the loopback transport only.</summary>
public sealed class DemoPanelContribution : IDevicePanelContribution
{
    public string Id => "demo";

    public string MenuTitle => "_Demo Device Panel...";

    public string? PresenterName => null;

    public bool IsAvailable(string transport, int vendorId, int productId) =>
        string.Equals(transport, "loopback", StringComparison.OrdinalIgnoreCase);

    public UiDefinition BuildDefinition() => DemoUiDefinition.Build();

    public IControlSurface CreateSurface(Session session) => new DemoControlSurface(session);
}
