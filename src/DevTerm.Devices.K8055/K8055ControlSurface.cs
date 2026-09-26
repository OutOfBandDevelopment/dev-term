using System.Globalization;
using DevTerm.Core.Control;
using DevTerm.Core.Sessions;

namespace DevTerm.Devices.K8055;

/// <summary>
/// <see cref="IControlSurface"/> for the Velleman K8055, sending commands over the given
/// <see cref="Session"/>'s live HID connection. Encodes per
/// docs/design/features/velleman-k8055-protocol.md: the "Set Analog/Digital outputs" (0x05) frame
/// carries the digital-out bitmask and both analog-out bytes *together*, so any digital-out or
/// analog-out mutation resends the full frame with the last-known state of everything else — the
/// real device sets all outputs at once, not one bit at a time. Trailing bytes 6-7
/// (duration/debounce, unconfirmed per the proposal doc) are sent as 0x00, 0x00 (static level, not
/// a pulse). The two reset-counter commands send their own fixed frame directly, with no shared
/// state. Every frame is sent with a leading <c>0x00</c> HID report-ID byte ahead of the 8-byte K8055
/// payload (a 9-byte write total) — the same convention confirmed live for
/// <c>DevTerm.Devices.Busylight</c> (Windows' HidD_SetOutputReport requires the report ID as the
/// buffer's first byte even for a device with no report IDs of its own), and consistent with the
/// K8055's own 9-byte *input* reports observed live
/// (<c>[00, 00, 03, AnalogIn1, ...]</c> — byte 0 is the same report-ID slot). The original
/// implementation omitted this byte and only ever verified that the write didn't throw, not that it
/// changed anything on the device — see docs/changes/2026-09-22.md's follow-up entry. Also an
/// <see cref="ICommandPreview"/>: <see cref="PreviewCommand"/> shows the exact report bytes (as hex)
/// an invocation would send, computed by the same <see cref="Plan"/> path <see cref="InvokeAsync"/>
/// uses, without committing the state change.
/// </summary>
public sealed class K8055ControlSurface : IControlSurface, ICommandPreview
{
    private const byte _setOutputsCommand = 0x05;
    private const byte _resetCounter1Command = 0x03;
    private const byte _resetCounter2Command = 0x04;

    private readonly Session _session;
    private readonly Lock _stateLock = new();
    private (byte DigitalOut, byte AnalogOut1, byte AnalogOut2) _state;

    public K8055ControlSurface(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        byte[] frame;
        lock (_stateLock)
        {
            (frame, _state) = Plan(commandId, value, _state);
        }

        return _session.SendAsync(frame, cancellationToken);
    }

    /// <summary>The report <see cref="InvokeAsync"/> would send, as hex bytes (report-ID byte included), without changing any output state; null for an unknown command or unparsable value.</summary>
    public string? PreviewCommand(string commandId, string? value)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        try
        {
            byte[] frame;
            lock (_stateLock)
            {
                (frame, _) = Plan(commandId, value, _state);
            }

            return CommandPreviewFormat.ToHex(frame);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// The one place a command id + value becomes a report and a new output state — shared by
    /// <see cref="InvokeAsync"/> (which commits the new state) and <see cref="PreviewCommand"/>
    /// (which discards it). Throws <see cref="ArgumentException"/> for an unknown command id.
    /// </summary>
    private static (byte[] Frame, (byte DigitalOut, byte AnalogOut1, byte AnalogOut2) NewState) Plan(
        string commandId,
        string? value,
        (byte DigitalOut, byte AnalogOut1, byte AnalogOut2) state)
    {
        if (TryParseDigitalOutChannel(commandId, out var channelIndex))
        {
            var mask = (byte)(1 << channelIndex);
            var digitalOut = ParseBool(value) ? (byte)(state.DigitalOut | mask) : (byte)(state.DigitalOut & ~mask);
            var newState = state with { DigitalOut = digitalOut };
            return (BuildSetOutputsFrame(newState), newState);
        }

        switch (commandId)
        {
            case "analogOut1":
                {
                    var newState = state with { AnalogOut1 = ParseByte(value) };
                    return (BuildSetOutputsFrame(newState), newState);
                }

            case "analogOut2":
                {
                    var newState = state with { AnalogOut2 = ParseByte(value) };
                    return (BuildSetOutputsFrame(newState), newState);
                }

            case "resetCounter1":
                return (BuildFixedFrame(_resetCounter1Command), state);
            case "resetCounter2":
                return (BuildFixedFrame(_resetCounter2Command), state);
            default:
                throw new ArgumentException($"Unknown K8055 command '{commandId}'.", nameof(commandId));
        }
    }

    private static bool TryParseDigitalOutChannel(string commandId, out int channelIndex)
    {
        channelIndex = -1;
        const string Prefix = "digitalOut";
        if (!commandId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(commandId.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var channel) || channel is < 1 or > 8)
        {
            return false;
        }

        channelIndex = channel - 1;
        return true;
    }

    private static bool ParseBool(string? value) => value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static byte ParseByte(string? value)
    {
        var number = double.Parse(value ?? "0", CultureInfo.InvariantCulture);
        return (byte)Math.Clamp(number, 0, 255);
    }

    private static byte[] BuildSetOutputsFrame((byte DigitalOut, byte AnalogOut1, byte AnalogOut2) state) =>
        [0x00, _setOutputsCommand, state.DigitalOut, state.AnalogOut1, state.AnalogOut2, 0x00, 0x00, 0x00, 0x00];

    private static byte[] BuildFixedFrame(byte command) => [0x00, command, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
}