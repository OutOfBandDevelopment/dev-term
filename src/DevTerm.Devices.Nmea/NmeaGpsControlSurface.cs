using DevTerm.Core.Control;

namespace DevTerm.Devices.Nmea;

/// <summary>
/// <see cref="IControlSurface"/> for a plain NMEA 0183 GPS receiver: a real no-op, the same reason
/// <c>De5000ControlSurface</c> is - a GPS receiver's NMEA output is unprompted and one-directional,
/// so <see cref="NmeaGpsUiDefinition"/> declares only indicators. Exists purely so
/// <c>ControlPanelMode</c>/<c>ControlPanelWindow</c> have something to construct;
/// <see cref="InvokeAsync"/> should never actually be called.
/// </summary>
public sealed class NmeaGpsControlSurface : IControlSurface
{
    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        throw new ArgumentException($"A plain NMEA GPS receiver has no writable commands; '{commandId}' is not supported.", nameof(commandId));
    }
}
