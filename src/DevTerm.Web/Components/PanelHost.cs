using DevTerm.Core.Control;
using DevTerm.UiDefinitions;

namespace DevTerm.Web.Components;

/// <summary>
/// The control panel the host serves: its definition plus the surface that carries a control's command to the device.
/// Registered empty before the host is built (the session it drives only exists afterwards), then filled by <see cref="Set"/>.
/// </summary>
public sealed class PanelHostHolder
{
    public UiDefinition? Definition { get; private set; }

    public IControlSurface? Surface { get; private set; }

    public void Set(UiDefinition definition, IControlSurface surface)
    {
        Definition = definition;
        Surface = surface;
    }
}
