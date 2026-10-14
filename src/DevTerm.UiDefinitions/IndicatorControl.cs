using System.Text.Json.Serialization;

namespace DevTerm.UiDefinitions;

/// <summary>How an <see cref="IndicatorControl"/> reads: plain, or as a warning (e.g. a "device not found" hint).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IndicatorStyle>))]
public enum IndicatorStyle
{
    Plain,

    /// <summary>Drawn as a warning where the front end can (WPF: the window's warning text color; the TUI has no per-label color and shows it plain).</summary>
    Warning,
}

/// <summary>
/// A read-only display bound to live decoder output, not a control the user changes (e.g. a
/// digital input's current state, a pulse counter value, a connection status line). In a generated
/// form, a read-only property of the model the form is bound to.
/// </summary>
public sealed class IndicatorControl : UiControl
{
    public string? DefaultValue { get; set; }

    public IndicatorStyle Style { get; set; } = IndicatorStyle.Plain;

    /// <summary>
    /// When set, an <see cref="Expression"/> deriving this indicator's displayed number from the
    /// live published values (e.g. <c>{raw_mv} / 1000</c>) instead of showing <see cref="UiControl.Id"/>'s
    /// own published text verbatim. Unset (the default) keeps today's direct behavior. See
    /// docs/design/features/manifest-editor-expression-builder.md.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>
    /// A .NET numeric format string (<c>0.00</c>, <c>X2</c> for hex, <c>P0</c>) applied to the number in the displayed
    /// text; null shows the text as published. Display only: charts and thresholds keep the raw value.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Appended after a formatted number (<c>V</c>, <c>mA</c>); null adds nothing.</summary>
    public string? Unit { get; set; }

    /// <summary>
    /// What a front end shows for <paramref name="published"/>: the number in it through <see cref="Format"/>, then
    /// <see cref="Unit"/>. Text with no number, or no hint set, comes back unchanged.
    /// </summary>
    public string Display(string published)
    {
        ArgumentNullException.ThrowIfNull(published);

        if (string.IsNullOrEmpty(Format) && string.IsNullOrEmpty(Unit))
        {
            return published;
        }

        if (!ChartValue.TryParse(published, out var number))
        {
            return published;
        }

        string text;
        try
        {
            text = !string.IsNullOrEmpty(Format) && Format[0] is 'x' or 'X'
                ? ((long)Math.Round(number)).ToString(Format, System.Globalization.CultureInfo.InvariantCulture)
                : number.ToString(string.IsNullOrEmpty(Format) ? "G" : Format, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return published;
        }

        return string.IsNullOrEmpty(Unit) ? text : $"{text} {Unit}";
    }
}
