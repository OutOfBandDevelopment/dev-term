using System.Globalization;
using DevTerm.Core.Control;
using DevTerm.Core.Sessions;

namespace DevTerm.Devices.Busylight;

/// <summary>
/// <see cref="IControlSurface"/> for the Kuando Busylight, sending the confirmed-working
/// single-command frame (9 bytes: report ID 0x00 + an 8-byte struct — NextStep, Repeat, Color R/G/B,
/// On, Off, a packed Audio byte) over the given <see cref="Session"/>'s live HID connection, per
/// docs/design/proposals/kuando-busylight-protocol.md. Every command other than "apply" only mutates
/// internal state; "apply" is the only command that actually sends a frame, matching the mockup's
/// explicit [Apply] button rather than sending on every field change. The "color" command accepts
/// either a known preset name (see <see cref="Colors"/>) or a custom <c>"r,g,b"</c> triple (each
/// 0-255, invariant culture) as sent by either front end's RGB/HSV color-picker modal (opened via
/// <c>ButtonControl.ColorPickerTargetCommandId</c> on the "Custom..." button — this surface never
/// shows UI itself). "customColor" itself is a documented no-op — the button opens a color picker
/// via <c>ColorPickerTargetCommandId</c> instead of invoking it. "programSequence" (the 64-byte
/// batch/program mode, checksum-correct per the proposal doc but confirmed to have no visible effect
/// on the real device) is still accepted here as a no-op for backward compatibility, but the
/// "Program Sequence..." button was removed from <see cref="BusylightUiDefinition"/> since a button
/// that does nothing on real hardware is worse than no button.
/// </summary>
public sealed class BusylightControlSurface : IControlSurface
{
    private static readonly IReadOnlyDictionary<string, (byte R, byte G, byte B)> Colors = new Dictionary<string, (byte R, byte G, byte B)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Red"] = (0xFF, 0x00, 0x00),
        ["Green"] = (0x00, 0xFF, 0x00),
        ["Blue"] = (0x00, 0x00, 0xFF),
        ["Yellow"] = (0xFF, 0xFF, 0x00),
        ["Off"] = (0x00, 0x00, 0x00),
    };

    private static readonly string[] Tracks = ["Funky", "Nordic", "Quiet", "Open Office", "Kuando"];

    private readonly Session _session;
    private readonly object _stateLock = new();
    private byte _r;
    private byte _g;
    private byte _b;
    private byte _on = 0x01;
    private byte _off;
    private bool _muted;
    private int _trackIndex;
    private byte _volume;

    public BusylightControlSurface(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (commandId is "customColor" or "programSequence")
        {
            return Task.CompletedTask;
        }

        if (commandId == "apply")
        {
            byte[] frame;
            lock (_stateLock)
            {
                frame = BuildFrame();
            }

            return _session.SendAsync(frame, cancellationToken);
        }

        lock (_stateLock)
        {
            switch (commandId)
            {
                case "color":
                    SetColor(value);
                    break;
                case "blinkMode":
                    SetBlinkPreset(value);
                    break;
                case "onMs":
                    _on = ParseByte(value);
                    break;
                case "offMs":
                    _off = ParseByte(value);
                    break;
                case "mute":
                    _muted = ParseBool(value);
                    break;
                case "track":
                    SetTrack(value);
                    break;
                case "volume":
                    _volume = ParseByte(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown Busylight command '{commandId}'.", nameof(commandId));
            }
        }

        return Task.CompletedTask;
    }

    private void SetColor(string? value)
    {
        if (value is null)
        {
            return;
        }

        if (Colors.TryGetValue(value, out var rgb))
        {
            (_r, _g, _b) = rgb;
            return;
        }

        if (TryParseRgbTriple(value, out var r, out var g, out var b))
        {
            (_r, _g, _b) = (r, g, b);
        }
    }

    private static bool TryParseRgbTriple(string value, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var parts = value.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out r)
            || !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out g)
            || !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out b))
        {
            return false;
        }

        return true;
    }

    private void SetBlinkPreset(string? value)
    {
        switch (value)
        {
            case "Solid":
                _on = 0x01;
                _off = 0x00;
                break;
            case "Slow":
                _on = 0x50;
                _off = 0x50;
                break;
            case "Fast":
                _on = 0x10;
                _off = 0x10;
                break;
        }
    }

    private void SetTrack(string? value)
    {
        var index = value is null ? -1 : Array.IndexOf(Tracks, value);
        if (index >= 0)
        {
            _trackIndex = index;
        }
    }

    private static bool ParseBool(string? value) => value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static byte ParseByte(string? value)
    {
        var number = double.Parse(value ?? "0", CultureInfo.InvariantCulture);
        return (byte)Math.Clamp(number, 0, 255);
    }

    private byte[] BuildFrame()
    {
        const byte NextStep = 0x00;
        const byte Repeat = 0x00;
        var playBit = _muted ? 0 : 1;
        var audio = (byte)((playBit << 7) | (_trackIndex << 3) | (_volume & 0x07));
        return [0x00, NextStep, Repeat, _r, _g, _b, _on, _off, audio];
    }
}
