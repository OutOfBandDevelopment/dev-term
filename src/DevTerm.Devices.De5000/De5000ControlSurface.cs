using DevTerm.Core.Control;

namespace DevTerm.Devices.De5000;

/// <summary>
/// <see cref="IControlSurface"/> for the DE-5000: a real no-op. Unlike every other device module
/// here, the meter has no documented computer-controllable command at all - its optical output is
/// unprompted and one-directional (see docs/design/proposals/de5000-lcr-meter-protocol.md), so
/// <see cref="De5000UiDefinition"/> declares only indicators, never a button/toggle/slider. This
/// exists purely so <c>ControlPanelMode</c>/<c>ControlPanelWindow</c> (which both take a non-nullable
/// <see cref="IControlSurface"/>) have something to construct; <see cref="InvokeAsync"/> should never
/// actually be called, and throws loudly rather than silently swallowing a call if it ever is.
/// </summary>
public sealed class De5000ControlSurface : IControlSurface
{
    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        throw new ArgumentException($"The DE-5000 has no writable commands; '{commandId}' is not supported.", nameof(commandId));
    }
}
