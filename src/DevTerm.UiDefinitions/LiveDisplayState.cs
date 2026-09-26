using System.Globalization;
using System.Text.RegularExpressions;

namespace DevTerm.UiDefinitions;

/// <summary>
/// The framework-agnostic live state behind one display control (<see cref="BarGraphControl"/>,
/// <see cref="StripChartControl"/>, <see cref="VectorControl"/>): both renderers feed it the
/// structured presenter's published values and draw from it, so what's plotted — clamping,
/// history, scaling, coordinate conversion — is decided once, here, and tested without a UI.
/// </summary>
public abstract class LiveDisplayState
{
    /// <summary>The published value ids this display reads.</summary>
    public abstract IReadOnlyList<string> ValueIds { get; }

    /// <summary>Applies one published value; false when the id isn't one of <see cref="ValueIds"/> or the text holds no number (nothing changes).</summary>
    public bool Apply(string id, string text) => ApplyAll([new KeyValuePair<string, string>(id, text)]);

    /// <summary>
    /// Applies one <c>ValuesChanged</c> batch as a single update (a vector moves once for an x+y
    /// pair published together, not once per coordinate); true when anything changed.
    /// </summary>
    public bool ApplyAll(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        OnBatchStarting();
        var changed = false;
        foreach (var (id, text) in values)
        {
            if (ValueIds.Contains(id, StringComparer.Ordinal) && ChartValue.TryParse(text, out var value))
            {
                changed |= ApplyNumber(id, value);
            }
        }

        OnBatchCompleted(changed);
        return changed;
    }

    protected abstract bool ApplyNumber(string id, double value);

    protected virtual void OnBatchStarting()
    {
    }

    protected virtual void OnBatchCompleted(bool changed)
    {
    }

    /// <summary>The state for <paramref name="control"/>, or null when it isn't a live display control.</summary>
    public static LiveDisplayState? For(UiControl control) => control switch
    {
        BarGraphControl bar => new BarGraphState(bar),
        StripChartControl strip => new StripChartState(strip),
        VectorControl vector => new VectorState(vector),
        _ => null,
    };
}

/// <summary>Reads a number out of a published value's text — tolerant of units or a prefix, since a raw device reply often carries them.</summary>
public static partial class ChartValue
{
    /// <summary>
    /// The whole text as an invariant-culture number (<c>"+1.2345E+00"</c>), else the first number
    /// found in it (<c>"DC VOLT 1.25 V"</c> → 1.25). False when there's none, or it's not finite.
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            var match = FirstNumber().Match(text);
            if (!match.Success || !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }
        }

        return double.IsFinite(value);
    }

    /// <summary>A short display form: up to 3 significant decimals, invariant culture.</summary>
    public static string Format(double value, string? unit = null) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(unit) ? string.Empty : " " + unit);

    [GeneratedRegex(@"[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?")]
    private static partial Regex FirstNumber();
}

/// <summary>A <see cref="BarGraphControl"/>'s latest value per channel.</summary>
public sealed class BarGraphState : LiveDisplayState
{
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);

    public BarGraphState(BarGraphControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Control = control;
        ValueIds = [.. control.Channels.Select(c => c.Id)];
    }

    public BarGraphControl Control { get; }

    public override IReadOnlyList<string> ValueIds { get; }

    /// <summary>The channel's latest value, or null before any has arrived.</summary>
    public double? ValueOf(string channelId) => _values.TryGetValue(channelId, out var value) ? value : null;

    /// <summary>How full the channel's bar is, in [0, 1] (0 before any value, and for an empty range).</summary>
    public double FractionOf(string channelId)
    {
        var range = Control.Maximum - Control.Minimum;
        if (ValueOf(channelId) is not { } value || range <= 0)
        {
            return 0;
        }

        return Math.Clamp((value - Control.Minimum) / range, 0, 1);
    }

    protected override bool ApplyNumber(string id, double value)
    {
        _values[id] = value;
        return true;
    }
}

/// <summary>A <see cref="StripChartControl"/>'s rolling history per channel.</summary>
public sealed class StripChartState : LiveDisplayState
{
    private readonly Dictionary<string, Queue<double>> _history = new(StringComparer.Ordinal);

    public StripChartState(StripChartControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Control = control;
        ValueIds = [.. control.Channels.Select(c => c.Id)];
        foreach (var id in ValueIds)
        {
            _history.TryAdd(id, new Queue<double>());
        }
    }

    public StripChartControl Control { get; }

    public override IReadOnlyList<string> ValueIds { get; }

    /// <summary>At least one sample is kept, whatever the definition says.</summary>
    public int Capacity => Math.Max(Control.HistoryLength, 1);

    /// <summary>The channel's samples, oldest first (at most <see cref="Capacity"/>).</summary>
    public IReadOnlyList<double> SamplesOf(string channelId) =>
        _history.TryGetValue(channelId, out var samples) ? [.. samples] : [];

    /// <summary>
    /// The value axis: the declared [Minimum, Maximum] when both are set, otherwise the visible
    /// history's own span (widened to at least ±0.5 around a flat line, and to [0, 1] with no data)
    /// — a declared bound still wins for its own end.
    /// </summary>
    public (double Minimum, double Maximum) Scale()
    {
        if (Control.Minimum is { } fixedMin && Control.Maximum is { } fixedMax && fixedMax > fixedMin)
        {
            return (fixedMin, fixedMax);
        }

        var all = _history.Values.SelectMany(q => q).ToList();
        if (all.Count == 0)
        {
            return (Control.Minimum ?? 0, Control.Maximum ?? 1);
        }

        var min = Control.Minimum ?? all.Min();
        var max = Control.Maximum ?? all.Max();
        if (max - min < 1e-9)
        {
            min -= 0.5;
            max += 0.5;
        }

        return (min, max);
    }

    protected override bool ApplyNumber(string id, double value)
    {
        var samples = _history[id];
        samples.Enqueue(value);
        while (samples.Count > Capacity)
        {
            samples.Dequeue();
        }

        return true;
    }
}

/// <summary>A <see cref="VectorControl"/>'s current point, trail, and optional color.</summary>
public sealed class VectorState : LiveDisplayState
{
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly Queue<(double X, double Y, double Z)> _trail = new();
    private (double X, double Y, double Z)? _pointBeforeBatch;
    private bool _coordinateChanged;

    public VectorState(VectorControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Control = control;
        ValueIds = [.. control.ValueIds().Distinct(StringComparer.Ordinal)];
    }

    public VectorControl Control { get; }

    public override IReadOnlyList<string> ValueIds { get; }

    /// <summary>Whether every coordinate value (x/y[/z], or r/theta) has arrived at least once.</summary>
    public bool HasPoint => Control.Coordinates switch
    {
        CoordinateSystem.Polar => Has(Control.RadiusId) && Has(Control.AngleId),
        CoordinateSystem.XYZ => Has(Control.XId) && Has(Control.YId) && Has(Control.ZId),
        _ => Has(Control.XId) && Has(Control.YId),
    };

    /// <summary>The current point in Cartesian coordinates (polar converted; z is 0 unless XYZ).</summary>
    public (double X, double Y, double Z) Point
    {
        get
        {
            if (Control.Coordinates == CoordinateSystem.Polar)
            {
                var r = Get(Control.RadiusId);
                var theta = Get(Control.AngleId);
                var radians = Control.AngleUnit == AngleUnit.Degrees ? theta * Math.PI / 180 : theta;
                return (r * Math.Cos(radians), r * Math.Sin(radians), 0);
            }

            return (Get(Control.XId), Get(Control.YId), Control.Coordinates == CoordinateSystem.XYZ ? Get(Control.ZId) : 0);
        }
    }

    /// <summary>Earlier points, oldest first, not including the current one.</summary>
    public IReadOnlyList<(double X, double Y, double Z)> Trail => [.. _trail];

    /// <summary>The live h/s/v color when a hue channel is declared and has arrived (saturation/value default to 1); otherwise null.</summary>
    public (byte R, byte G, byte B)? Color
    {
        get
        {
            if (Control.HueId is null || !Has(Control.HueId))
            {
                return null;
            }

            static double Unit(double v) => v > 1 ? v / 100 : v;
            var s = Control.SaturationId is { } sid && Has(sid) ? Unit(Get(sid)) : 1;
            var v = Control.BrightnessId is { } vid && Has(vid) ? Unit(Get(vid)) : 1;
            return ChartPalette.FromHsv(Get(Control.HueId), s, v);
        }
    }

    /// <summary>A one-line readout, e.g. <c>x=0.5 y=-0.25</c> or <c>r=0.8 θ=45°</c> — "—" for a value not yet received.</summary>
    public string Readout()
    {
        string V(string? id) => id is not null && Has(id) ? ChartValue.Format(Get(id)) : "—";
        return Control.Coordinates switch
        {
            CoordinateSystem.Polar => $"r={V(Control.RadiusId)} θ={V(Control.AngleId)}{(Control.AngleUnit == AngleUnit.Degrees ? "°" : " rad")}",
            CoordinateSystem.XYZ => $"x={V(Control.XId)} y={V(Control.YId)} z={V(Control.ZId)}",
            _ => $"x={V(Control.XId)} y={V(Control.YId)}",
        };
    }

    protected override void OnBatchStarting()
    {
        _pointBeforeBatch = HasPoint ? Point : null;
        _coordinateChanged = false;
    }

    protected override bool ApplyNumber(string id, double value)
    {
        _coordinateChanged |= id != Control.HueId && id != Control.SaturationId && id != Control.BrightnessId;
        _values[id] = value;
        return true;
    }

    protected override void OnBatchCompleted(bool changed)
    {
        // The point moved: where it was joins the trail (bounded by TrailLength).
        if (_coordinateChanged && _pointBeforeBatch is { } previous && Control.TrailLength > 0)
        {
            _trail.Enqueue(previous);
            while (_trail.Count > Control.TrailLength)
            {
                _trail.Dequeue();
            }
        }
    }

    private bool Has(string? id) => id is not null && _values.ContainsKey(id);

    private double Get(string? id) => id is not null && _values.TryGetValue(id, out var v) ? v : 0;
}
