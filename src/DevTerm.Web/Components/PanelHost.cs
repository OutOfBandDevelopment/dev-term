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

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>The latest published value per indicator id (what a freshly opened page starts from).</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>Raised after a batch of published values was merged into <see cref="Values"/>.</summary>
    public event Action? ValuesChanged;

    /// <summary>Merges a structured presenter's <c>ValuesChanged</c> batch and tells every open panel to re-render.</summary>
    public void Publish(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (id, value) in values)
        {
            _values[id] = value;
        }

        ValuesChanged?.Invoke();
    }

    public void Set(UiDefinition definition, IControlSurface surface)
    {
        Definition = definition;
        Surface = surface;
    }
}
