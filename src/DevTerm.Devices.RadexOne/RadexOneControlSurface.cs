using System.Globalization;
using DevTerm.Core.Control;
using DevTerm.Core.Sessions;

namespace DevTerm.Devices.RadexOne;

/// <summary>
/// <see cref="IControlSurface"/> for the Radex One geiger counter, sending framed requests (see
/// <see cref="RadexOneFramer"/> and <see cref="RadexOneExtensionCodec"/>) over the given
/// <see cref="Session"/>'s live serial connection (2400 8N1, no handshake — real-hardware confirmed
/// 2026-09-25 on COM8), per docs/design/proposals/radex-one-protocol.md. The device is a plain
/// virtual COM port; there is no HID report wrapping (an earlier "confirmed directly" HID assumption
/// in that doc was wrong). "readData"/"readSerialVersion"/"readSettings" are one-shot queries built
/// via <see cref="RadexOneExtensionCodec.BuildQuery"/>; their replies arrive asynchronously through
/// <see cref="RadexOneDecoder"/> on the session's normal output path, not through this surface.
/// "alarmMode"/"threshold" only mutate local state; "writeSettings" is the only command that sends a
/// Write Settings request, and sends it three times in a row per the proposal's documented "must be
/// sent 3x for the device to accept it" quirk — a real-device gotcha worth keeping even though it
/// can't be verified against actual hardware acceptance behavior without the device attached.
/// Also an <see cref="ICommandPreview"/>: shows the exact packet bytes a query/apply would send.
/// </summary>
public sealed class RadexOneControlSurface : IControlSurface, ICommandPreview
{
    private readonly Session _session;
    private readonly Lock _stateLock = new();
    private int _nextPacketNumber = 1;
    private byte _alarmMode;
    private ushort _threshold;

    public RadexOneControlSurface(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        switch (commandId)
        {
            case "alarmMode":
                lock (_stateLock)
                {
                    _alarmMode = ParseAlarmMode(value);
                }

                return Task.CompletedTask;

            case "threshold":
                lock (_stateLock)
                {
                    _threshold = ParseUInt16(value);
                }

                return Task.CompletedTask;

            case "readData":
                return SendQueryAsync(RadexOneCommand.ReadData, cancellationToken);
            case "readSerialVersion":
                return SendQueryAsync(RadexOneCommand.ReadSerialVersion, cancellationToken);
            case "readSettings":
                return SendQueryAsync(RadexOneCommand.ReadSettings, cancellationToken);
            case "writeSettings":
                return SendWriteSettingsAsync(cancellationToken);
            default:
                throw new ArgumentException($"Unknown Radex One command '{commandId}'.", nameof(commandId));
        }
    }

    /// <summary>The framed packet a query/apply would send right now, as hex bytes; null for a state-only setter or an unknown command.</summary>
    public string? PreviewCommand(string commandId, string? value)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        return commandId switch
        {
            "readData" => PreviewQuery(RadexOneCommand.ReadData),
            "readSerialVersion" => PreviewQuery(RadexOneCommand.ReadSerialVersion),
            "readSettings" => PreviewQuery(RadexOneCommand.ReadSettings),
            "writeSettings" => PreviewWriteSettings(),
            _ => null,
        };
    }

    private async Task SendQueryAsync(ushort commandType, CancellationToken cancellationToken)
    {
        var report = BuildQueryReport(commandType, TakePacketNumber());
        await _session.SendAsync(report, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendWriteSettingsAsync(CancellationToken cancellationToken)
    {
        byte[] report;
        lock (_stateLock)
        {
            report = RadexOneFramer.BuildRequest((ushort)TakePacketNumberLocked(), RadexOneExtensionCodec.BuildWriteSettings(_alarmMode, _threshold));
        }

        // Real-device gotcha (docs/design/proposals/radex-one-protocol.md): a single Write Settings
        // request silently doesn't take effect — the device requires it three times in a row.
        for (var i = 0; i < 3; i++)
        {
            await _session.SendAsync(report, cancellationToken).ConfigureAwait(false);
        }
    }

    private string PreviewQuery(ushort commandType)
    {
        lock (_stateLock)
        {
            return CommandPreviewFormat.ToHex(BuildQueryReport(commandType, _nextPacketNumber));
        }
    }

    private string PreviewWriteSettings()
    {
        lock (_stateLock)
        {
            var packet = RadexOneFramer.BuildRequest((ushort)_nextPacketNumber, RadexOneExtensionCodec.BuildWriteSettings(_alarmMode, _threshold));
            return CommandPreviewFormat.ToHex(packet);
        }
    }

    private static byte[] BuildQueryReport(ushort commandType, int packetNumber) =>
        RadexOneFramer.BuildRequest((ushort)packetNumber, RadexOneExtensionCodec.BuildQuery(commandType));

    private int TakePacketNumber()
    {
        lock (_stateLock)
        {
            return TakePacketNumberLocked();
        }
    }

    private int TakePacketNumberLocked()
    {
        var packetNumber = _nextPacketNumber;
        _nextPacketNumber = (ushort)(_nextPacketNumber + 1);
        return packetNumber;
    }

    private static byte ParseAlarmMode(string? value) => value switch
    {
        "Vibration" => 1,
        "Audio" => 2,
        "Vibration+Audio" => 3,
        _ => 0,
    };

    private static ushort ParseUInt16(string? value)
    {
        var number = double.Parse(value ?? "0", CultureInfo.InvariantCulture);
        return (ushort)Math.Clamp(number, 0, ushort.MaxValue);
    }
}
