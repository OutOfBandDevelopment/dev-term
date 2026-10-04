using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Core.Control;

/// <summary>
/// A device control panel a plugin contributes (docs/design/proposals/plugin-contributed-panels.md): register one from an
/// <see cref="Plugins.IPluginModule"/> and every front end adds a Device-menu entry for it, enabled when the connection
/// suits the device. The panel itself is the generic <see cref="UiDefinition"/> renderer, so a contribution is data plus a surface.
/// </summary>
public interface IDevicePanelContribution
{
    /// <summary>A stable id (also what <c>Web:Panel</c> selects), e.g. "k8055".</summary>
    string Id { get; }

    /// <summary>The Device menu text, with an <c>_</c> before the access key, e.g. "_K8055 Control Panel...".</summary>
    string MenuTitle { get; }

    /// <summary>The structured presenter that feeds the panel's indicators, or null when it has none.</summary>
    string? PresenterName { get; }

    /// <summary>Whether the panel makes sense for a connection (the menu item is disabled otherwise). Called only while connected.</summary>
    bool IsAvailable(string transport, int vendorId, int productId);

    UiDefinition BuildDefinition();

    IControlSurface CreateSurface(Session session);
}
