using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;
using DevTerm.Logging;

namespace DevTerm.DeviceManifests;

/// <summary>
/// Real values a device published, per value path, in the order they arrived: the top preference tier for
/// <see cref="SampleDataGenerator"/> (a recorded session, then a pattern's declared example, then generated values).
/// Built by running a recording's received bytes through the manifest's own presenters, so a path holds exactly what a live
/// panel would have been given. See docs/design/features/expression-picker-paths-and-cel.md.
/// </summary>
public sealed partial class RecordedSamples
{
    private readonly Dictionary<string, List<string>> _series = new(StringComparer.Ordinal);

    /// <summary>The paths with at least one recorded value.</summary>
    public IReadOnlyCollection<string> Paths => _series.Keys;

    /// <summary>True when nothing was recorded for any path (the log had no data this manifest recognises).</summary>
    public bool IsEmpty => _series.Count == 0;

    /// <summary>How many values were recorded for <paramref name="path"/>.</summary>
    public int Count(string path) => _series.TryGetValue(path, out var list) ? list.Count : 0;

    /// <summary>The recorded text at <paramref name="step"/> (wrapping around at the end), or null when the path has none.</summary>
    public string? Text(string path, int step) =>
        _series.TryGetValue(path, out var list) && list.Count > 0 ? list[((step % list.Count) + list.Count) % list.Count] : null;

    /// <summary>The number read from the recorded text at <paramref name="step"/> (<c>"12.5 V"</c> reads as 12.5), or null when it has no number.</summary>
    public double? Number(string path, int step)
    {
        if (Text(path, step) is not { } text)
        {
            return null;
        }

        var match = LeadingNumber().Match(text);
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    /// <summary>Runs received chunks through the manifest's reply and frame presenters and keeps every value they publish.</summary>
    public static RecordedSamples FromChunks(DeviceManifest manifest, IEnumerable<ReadOnlyMemory<byte>> chunks)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(chunks);

        var samples = new RecordedSamples();
        void Collect(object? _, IReadOnlyDictionary<string, string> values)
        {
            foreach (var (path, value) in values)
            {
                if (!samples._series.TryGetValue(path, out var list))
                {
                    samples._series[path] = list = [];
                }

                list.Add(value);
            }
        }

        var reply = new ManifestReplyPresenter(manifest);
        reply.ValuesChanged += Collect;
        var frame = manifest.Inbound?.Frame is { } schema && schema.Validate().Count == 0 ? new ManifestFramePresenter(schema) : null;
        if (frame is not null)
        {
            frame.ValuesChanged += Collect;
        }

        foreach (var chunk in chunks)
        {
            _ = reply.Render(new ReadOnlySequence<byte>(chunk));
            _ = frame?.Render(new ReadOnlySequence<byte>(chunk));
        }

        return samples;
    }

    /// <summary>The same, from a session log's received (<c>rx</c>) records.</summary>
    public static RecordedSamples FromSessionLog(DeviceManifest manifest, SessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return FromChunks(manifest, log.Records.Where(r => r.Kind == SessionLogRecordKind.Rx).Select(r => r.Data));
    }

    [GeneratedRegex(@"^\s*[-+]?\d+(\.\d+)?([eE][-+]?\d+)?")]
    private static partial Regex LeadingNumber();
}
