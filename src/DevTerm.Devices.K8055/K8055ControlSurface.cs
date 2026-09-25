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
/// changed anything on the device — see docs/changes/2026-09-22.md's follow-up entry.
/// </summary>
public sealed class K8055ControlSurface : IControlSurface
{
    private const byte _setOutputsCommand = 0x05;
    private const byte _resetCounter1Command = 0x03;
    private const byte _resetCounter2Command = 0x04;

    private readonly Session _session;
    private readonly Lock _stateLock = new();
    private readonly bool[] _digitalOut = new bool[8];
    private byte _analogOut1;
    private byte _analogOut2;

    public K8055ControlSurface(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (TryParseDigitalOutChannel(commandId, out var channelIndex))
        {
            return SetDigitalOutAsync(channelIndex, ParseBool(value), cancellationToken);
        }

        return commandId switch
        {
            "analogOut1" => SetAnalogOutAsync(ParseByte(value), null, cancellationToken),
            "analogOut2" => SetAnalogOutAsync(null, ParseByte(value), cancellationToken),
            "resetCounter1" => SendFixedFrameAsync(_resetCounter1Command, cancellationToken),
            "resetCounter2" => SendFixedFrameAsync(_resetCounter2Command, cancellationToken),
            _ => throw new ArgumentException($"Unknown K8055 command '{commandId}'.", nameof(commandId)),
        };
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

    private Task SetDigitalOutAsync(int channelIndex, bool isOn, CancellationToken cancellationToken)
    {
        byte[] frame;
        lock (_stateLock)
        {
            _digitalOut[channelIndex] = isOn;
            frame = BuildSetOutputsFrame();
        }

        return _session.SendAsync(frame, cancellationToken);
    }

    private Task SetAnalogOutAsync(byte? analogOut1, byte? analogOut2, CancellationToken cancellationToken)
    {
        byte[] frame;
        lock (_stateLock)
        {
            if (analogOut1 is { } a1)
            {
                _analogOut1 = a1;
            }

            if (analogOut2 is { } a2)
            {
                _analogOut2 = a2;
            }

            frame = BuildSetOutputsFrame();
        }

        return _session.SendAsync(frame, cancellationToken);
    }

    private byte[] BuildSetOutputsFrame()
    {
        byte digitalOutByte = 0;
        for (var i = 0; i < _digitalOut.Length; i++)
        {
            if (_digitalOut[i])
            {
                digitalOutByte |= (byte)(1 << i);
            }
        }

        return [0x00, _setOutputsCommand, digitalOutByte, _analogOut1, _analogOut2, 0x00, 0x00, 0x00, 0x00];
    }

    private Task SendFixedFrameAsync(byte command, CancellationToken cancellationToken) =>
        _session.SendAsync(new byte[] { 0x00, command, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, cancellationToken);
}
