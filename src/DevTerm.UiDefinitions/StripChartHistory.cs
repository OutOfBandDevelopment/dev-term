using System.Globalization;
using System.Text;

namespace DevTerm.UiDefinitions;

/// <summary>
/// A <see cref="StripChartState"/>'s kept samples as a table, a CSV, and a per-sample readout, so every
/// front end shows and exports the same numbers. Samples carry no timestamps, so a row is identified by
/// its age: 0 is the newest sample, 1 the one before it, and so on (the chart's right edge is age 0).
/// </summary>
public static class StripChartHistory
{
    /// <summary>The header row: <c>Age</c>, then each channel's label (its id when unlabelled).</summary>
    public static IReadOnlyList<string> Header(StripChartState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return ["Age", .. state.Control.Channels.Select(c => c.Label ?? c.Id)];
    }

    /// <summary>
    /// One row per kept sample, oldest first: its age, then each channel's value (invariant culture, round-trip
    /// precision) or an empty cell where a channel has no sample that far back.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Rows(StripChartState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var channels = state.Control.Channels.Select(c => state.SamplesOf(c.Id)).ToList();
        var length = channels.Count == 0 ? 0 : channels.Max(s => s.Count);
        var rows = new List<IReadOnlyList<string>>(length);
        for (var age = length - 1; age >= 0; age--)
        {
            var row = new List<string> { age.ToString(CultureInfo.InvariantCulture) };
            foreach (var samples in channels)
            {
                var index = samples.Count - 1 - age;
                row.Add(index >= 0 ? samples[index].ToString("R", CultureInfo.InvariantCulture) : string.Empty);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The header and <see cref="Rows"/> as RFC 4180 CSV (CRLF line ends, quoting a label that needs it).</summary>
    public static string ToCsv(StripChartState state)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Header(state).Select(Quote))).Append("\r\n");
        foreach (var row in Rows(state))
        {
            builder.Append(string.Join(',', row)).Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>The same table as fixed-width text (columns right-aligned), for a read-only view.</summary>
    public static string ToText(StripChartState state)
    {
        var header = Header(state);
        var rows = Rows(state);
        var widths = header.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        var builder = new StringBuilder();
        void Line(IReadOnlyList<string> cells) =>
            builder.Append(string.Join("  ", cells.Select((c, i) => c.PadLeft(widths[i])))).Append('\n');

        Line(header);
        foreach (var row in rows)
        {
            Line(row);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The readout for one horizontal slot of the plot (0 is the oldest of <see cref="StripChartState.Capacity"/>, the
    /// last the newest): its age and each channel's value there, or null where no channel has a sample.
    /// </summary>
    public static string? ReadoutAt(StripChartState state, int slot)
    {
        ArgumentNullException.ThrowIfNull(state);
        var age = state.Capacity - 1 - slot;
        var parts = new List<string>();
        foreach (var channel in state.Control.Channels)
        {
            var samples = state.SamplesOf(channel.Id);
            var index = samples.Count - 1 - age;
            if (age >= 0 && index >= 0 && index < samples.Count)
            {
                parts.Add($"{channel.Label ?? channel.Id} {ChartValue.Format(samples[index], state.Control.Unit)}");
            }
        }

        return parts.Count == 0 ? null : $"{(age == 0 ? "now" : $"-{age}")}: {string.Join("  ", parts)}";
    }

    private static string Quote(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
}
