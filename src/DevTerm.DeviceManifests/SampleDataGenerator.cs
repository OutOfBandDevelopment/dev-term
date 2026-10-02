using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>
/// Produces realistic, deterministic sample values for the paths in a <see cref="ValuePathCatalog"/>, so an expression's
/// result (and a manifest editor preview) can be shown without a connected device. Numbers follow the path's
/// Minimum/Maximum as a smooth walk plus a little noise, booleans alternate, choices cycle; text paths are not numeric
/// and are given by <see cref="TextValues"/> instead of <see cref="Values"/>. A path with a declared example
/// (<see cref="ResponsePattern.Example"/>) starts from it: step 0 is the example itself, text paths keep it, and later numeric steps
/// wander within a few percent of it. The same seed and step always give the
/// same values, so screenshots and tests are stable. See docs/design/proposals/expression-picker-paths-and-cel.md.
/// </summary>
public static class SampleDataGenerator
{
    // A path with no declared range still gets a plausible span rather than a flat zero.
    private const double _defaultMinimum = 0;
    private const double _defaultMaximum = 100;

    /// <summary>The values at one point in time. Step 0, 1, 2... walks the series forward.</summary>
    public static IReadOnlyDictionary<string, double> Values(IEnumerable<ValuePath> paths, int seed = 0, int step = 0, RecordedSamples? recorded = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (Value(path, seed, step, recorded) is { } value)
            {
                values[path.Path] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// Sample text for each text path that has no choices (a decoder's string capture), stable by seed and step
    /// (<c>"sample-ab12"</c>), so a text expression's result can be previewed.
    /// </summary>
    public static IReadOnlyDictionary<string, string> TextValues(IEnumerable<ValuePath> paths, int seed = 0, int step = 0, RecordedSamples? recorded = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths.Where(p => p.Type == ValuePathType.Text && p.Choices is not { Count: > 0 }))
        {
            values[path.Path] = recorded?.Text(path.Path, step) ?? path.Example ?? $"sample-{Hash(seed, path.Path) % 0x10000:x4}-{step}";
        }

        return values;
    }

    /// <summary>One path's value at a step, or null when it has no numeric form (text).</summary>
    public static double? Value(ValuePath path, int seed = 0, int step = 0, RecordedSamples? recorded = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (recorded?.Number(path.Path, step) is { } recordedNumber)
        {
            return recordedNumber;
        }

        var hash = Hash(seed, path.Path);
        var phase = (hash % 628) / 100.0;

        switch (path.Type)
        {
            case ValuePathType.Boolean:
                return (step + (int)(hash % 2)) % 2;

            case ValuePathType.Number when ExampleNumber(path) is { } example:
                if (step == 0)
                {
                    return example;
                }

                var drift = Math.Sin((step * 0.35) + phase) * 0.05 * (Math.Abs(example) > 0 ? Math.Abs(example) : 1);
                return Math.Round(example + drift, SignificantDecimals(Math.Abs(example)));

            case ValuePathType.Number:
                var min = path.Minimum ?? _defaultMinimum;
                var max = path.Maximum ?? _defaultMaximum;
                if (max < min)
                {
                    (min, max) = (max, min);
                }

                var span = max - min;
                var wave = (Math.Sin((step * 0.35) + phase) + 1) / 2;
                var noise = (Math.Sin((step * 2.3) + (phase * 7)) * 0.03);
                var fraction = Math.Clamp(wave + noise, 0, 1);
                return Math.Round(min + (span * fraction), SignificantDecimals(span));

            default:
                return path.Choices is { Count: > 0 } choices ? step % choices.Count : null;
        }
    }

    /// <summary>The choice (or boolean) text a path shows at a step, for displaying alongside the numeric index.</summary>
    public static string? Text(ValuePath path, int step = 0) =>
        path.Choices is { Count: > 0 } choices ? choices[step % choices.Count] : null;

    private static double? ExampleNumber(ValuePath path) =>
        double.TryParse(path.Example, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int SignificantDecimals(double span) => span switch
    {
        >= 100 => 0,
        >= 10 => 1,
        >= 1 => 2,
        _ => 4,
    };

    // FNV-1a: string.GetHashCode is randomized per process, which would make "deterministic by seed" untrue.
    private static uint Hash(int seed, string text)
    {
        var hash = 2166136261u ^ (uint)seed;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }
}
