using System.Globalization;
using System.Text;

namespace DevTerm.Logging.Playback;

/// <summary>What a playback line shows — the front ends style by it (WPF colors, the TUI's text tags).</summary>
public enum PlaybackLineKind
{
    /// <summary>Received data as a presenter rendered it: <c>[ascii] text</c>.</summary>
    Received,

    /// <summary>Sent data: <c>[tx] text</c>.</summary>
    Sent,

    /// <summary>A connection event: <c>[dev-term] Connected.</c>.</summary>
    Status,

    /// <summary>A lost connection or a presenter failure: <c>[error] …</c>.</summary>
    Error,

    /// <summary>A playback annotation: <c>[note] …</c>.</summary>
    Note,
}

/// <summary>One display line of playback output.</summary>
public sealed record PlaybackLine(string Text, PlaybackLineKind Kind)
{
    public override string ToString() => Text;
}

/// <summary>
/// Turns played records into display lines, identically for every front end (and the CLI's
/// <c>--playback</c> output): <c>{offset} [{source}] {text}</c>, where the offset is log time from the
/// first record (<c>mm:ss.fff</c>, or <c>h:mm:ss.fff</c> past an hour).
/// </summary>
public static class PlaybackText
{
    public static IReadOnlyList<PlaybackLine> Lines(PlaybackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var time = FormatOffset(item.Offset);
        var record = item.Record;
        switch (record.Kind)
        {
            case SessionLogRecordKind.Rx:
                return [.. item.Outputs.Select(o => new PlaybackLine(
                    $"{time} [{o.PresenterName}] {o.Text}",
                    o.PresenterName == "error" ? PlaybackLineKind.Error : PlaybackLineKind.Received))];
            case SessionLogRecordKind.Tx:
                return [new($"{time} [tx] {Escape(record.Data.Span)}", PlaybackLineKind.Sent)];
            case SessionLogRecordKind.Open:
                return [new($"{time} [dev-term] Connected.", PlaybackLineKind.Status)];
            case SessionLogRecordKind.Close:
                return [new($"{time} [dev-term] Disconnected.", PlaybackLineKind.Status)];
            case SessionLogRecordKind.Disconnect:
                return [new(
                    record.Text is { } error ? $"{time} [error] Connection lost: {error}" : $"{time} [error] The device closed the connection.",
                    PlaybackLineKind.Error)];
            case SessionLogRecordKind.Session:
                var subject = record.Profile is { Length: > 0 } profile ? $"{profile} ({record.Connection})" : record.Connection ?? "(unknown connection)";
                return [new($"{time} [dev-term] Logging {subject}, connection {record.State ?? "unknown"}.", PlaybackLineKind.Status)];
            case SessionLogRecordKind.Note:
                return [new($"{time} [note] {record.Text}", PlaybackLineKind.Note)];
            default:
                return [];
        }
    }

    public static IEnumerable<PlaybackLine> Lines(PlaybackBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return batch.Items.SelectMany(Lines);
    }

    public static string FormatOffset(TimeSpan offset) =>
        offset.TotalHours >= 1
            ? offset.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : offset.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);

    /// <summary>
    /// Sent bytes as readable text: printable ASCII as-is, <c>\r</c>/<c>\n</c>/<c>\t</c>/<c>\\</c>
    /// escaped, anything else as <c>\xNN</c> — deliberately not run through a presenter, since
    /// feeding sent bytes to a stateful presenter would change what it renders for the received
    /// stream (live, presenters only ever see received bytes).
    /// </summary>
    public static string Escape(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            switch (b)
            {
                case (byte)'\r':
                    text.Append(@"\r");
                    break;
                case (byte)'\n':
                    text.Append(@"\n");
                    break;
                case (byte)'\t':
                    text.Append(@"\t");
                    break;
                case (byte)'\\':
                    text.Append(@"\\");
                    break;
                case >= 0x20 and < 0x7F:
                    text.Append((char)b);
                    break;
                default:
                    text.Append(CultureInfo.InvariantCulture, $@"\x{b:X2}");
                    break;
            }
        }

        return text.ToString();
    }
}
