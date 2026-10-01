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

        return OnBatchCompleted(changed);
    }

    protected abstract bool ApplyNumber(string id, double value);

    protected virtual void OnBatchStarting()
    {
    }

    /// <summary>
    /// Runs after every raw id in the batch has reached <see cref="ApplyNumber"/> — lets a subclass
    /// report a change that didn't come from <see cref="ApplyNumber"/>'s own return value (e.g. an
    /// expression-backed <see cref="StripChartState"/> channel, which only knows it has a complete
    /// derived sample once the whole batch has landed). Returns the final "did anything change"
    /// verdict <see cref="ApplyAll"/> reports back to its caller; the default passes
    /// <paramref name="changed"/> through unchanged.
    /// </summary>
    protected virtual bool OnBatchCompleted(bool changed) => changed;

    /// <summary>The state for <paramref name="control"/>, or null when it isn't a live display control.</summary>
    public static LiveDisplayState? For(UiControl control) => control switch
    {
        BarGraphControl bar => new BarGraphState(bar),
        StripChartControl strip => new StripChartState(strip),
        VectorControl vector => new VectorState(vector),
        IndicatorControl { Expression: { Length: > 0 } } indicator => new IndicatorState(indicator),
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
    private readonly Dictionary<string, Expression?> _expressionByChannel = new(StringComparer.Ordinal);

    public BarGraphState(BarGraphControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Control = control;

        var ids = new List<string>();
        foreach (var channel in control.Channels)
        {
            if (channel.Expression is { Length: > 0 } text && Expression.TryParse(text, out var expr, out _))
            {
                _expressionByChannel[channel.Id] = expr;
                ids.AddRange(expr!.ReferencedIds);
            }
            else
            {
                _expressionByChannel[channel.Id] = null;
                ids.Add(channel.Id);
            }
        }

        ValueIds = [.. ids.Distinct(StringComparer.Ordinal)];
    }

    public BarGraphControl Control { get; }

    public override IReadOnlyList<string> ValueIds { get; }

    /// <summary>
    /// The channel's latest value — either its own raw published value, or (when the channel
    /// declares an <see cref="ChartChannel.Expression"/>) that expression evaluated against every
    /// raw id it references — or null before any relevant raw value has arrived.
    /// </summary>
    public double? ValueOf(string channelId)
    {
        if (!_expressionByChannel.TryGetValue(channelId, out var expression))
        {
            return null;
        }

        if (expression is null)
        {
            return _values.TryGetValue(channelId, out var value) ? value : null;
        }

        return expression.ReferencedIds.Any(id => _values.ContainsKey(id)) ? expression.Evaluate(_values) : null;
    }

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
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Expression?> _expressionByChannel = new(StringComparer.Ordinal);
    private readonly HashSet<string> _touchedExpressionChannelsThisBatch = new(StringComparer.Ordinal);

    public StripChartState(StripChartControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Control = control;

        var ids = new List<string>();
        foreach (var channel in control.Channels)
        {
            _history.TryAdd(channel.Id, new Queue<double>());
            if (channel.Expression is { Length: > 0 } text && Expression.TryParse(text, out var expr, out _))
            {
                _expressionByChannel[channel.Id] = expr;
                ids.AddRange(expr!.ReferencedIds);
            }
            else
            {
                _expressionByChannel[channel.Id] = null;
                ids.Add(channel.Id);
            }
        }

        ValueIds = [.. ids.Distinct(StringComparer.Ordinal)];
    }

    public StripChartControl Control { get; }

    public override IReadOnlyList<string> ValueIds { get; }

    /// <summary>
    /// Hard ceiling on <see cref="Capacity"/>, regardless of what a manifest's
    /// <see cref="StripChartControl.HistoryLength"/> declares — protects against an
    /// unbounded-memory manifest (a per-sample <c>double</c> queue with no upper bound). See
    /// docs/bugs/fixed/050-strip-chart-history-unbounded.md.
    /// </summary>
    public const int MaxCapacity = 10_000;

    /// <summary>At least one sample is kept, and at most <see cref="MaxCapacity"/>, whatever the definition says.</summary>
    public int Capacity => Math.Clamp(Control.HistoryLength, 1, MaxCapacity);

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

    protected override void OnBatchStarting() => _touchedExpressionChannelsThisBatch.Clear();

    /// <summary>
    /// A plain channel enqueues one sample per raw arrival, exactly as before. An expression
    /// channel instead just notes it was touched — several raw ids it references can arrive in the
    /// same batch, and it must contribute exactly one derived sample for that batch, not one per raw
    /// id — the actual enqueue happens once in <see cref="OnBatchCompleted"/>, after every raw id in
    /// the batch has landed in <see cref="_values"/>.
    /// </summary>
    protected override bool ApplyNumber(string id, double value)
    {
        _values[id] = value;
        var changed = false;
        foreach (var (channelId, expression) in _expressionByChannel)
        {
            if (expression is null)
            {
                if (channelId == id)
                {
                    Enqueue(channelId, value);
                    changed = true;
                }
            }
            else if (expression.ReferencedIds.Contains(id, StringComparer.Ordinal))
            {
                _touchedExpressionChannelsThisBatch.Add(channelId);
            }
        }

        return changed;
    }

    protected override bool OnBatchCompleted(bool changed)
    {
        foreach (var channelId in _touchedExpressionChannelsThisBatch)
        {
            Enqueue(channelId, _expressionByChannel[channelId]!.Evaluate(_values));
            changed = true;
        }

        return changed;
    }

    private void Enqueue(string channelId, double value)
    {
        var samples = _history[channelId];
        samples.Enqueue(value);
        while (samples.Count > Capacity)
        {
            samples.Dequeue();
        }
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

    protected override bool OnBatchCompleted(bool changed)
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

        return changed;
    }

    private bool Has(string? id) => id is not null && _values.ContainsKey(id);

    private double Get(string? id) => id is not null && _values.TryGetValue(id, out var v) ? v : 0;
}

/// <summary>
/// An <see cref="IndicatorControl"/> whose <see cref="IndicatorControl.Expression"/> is set: derives
/// its displayed text from the live published values instead of showing its own raw id's text
/// verbatim. Only constructed by <see cref="LiveDisplayState.For"/> when the expression is set and
/// parses; an <see cref="IndicatorControl"/> with no expression never needs this class at all, so the
/// existing direct "set the label from the raw published id" path stays exactly as it was.
/// </summary>
public sealed class IndicatorState : LiveDisplayState
{
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly Expression? _expression;

    /// <summary>
    /// Never throws, even when <see cref="IndicatorControl.Expression"/> fails to parse — a bad
    /// expression leaves this state with no <see cref="ValueIds"/> and an always-null
    /// <see cref="Text"/> rather than taking down the control panel that built it. The load-time
    /// manifest validator is what actually catches a bad expression as an error; this is just the
    /// runtime fallback for one that somehow got through anyway.
    /// </summary>
    public IndicatorState(IndicatorControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Control = control;
        _ = Expression.TryParse(control.Expression ?? string.Empty, out _expression, out _);
        ValueIds = _expression?.ReferencedIds ?? [];
    }

    public IndicatorControl Control { get; }

    public override IReadOnlyList<string> ValueIds { get; }

    /// <summary>The expression evaluated against every value received so far, or null before any referenced id has arrived (or the expression failed to parse).</summary>
    public string? Text => _expression is not null && ValueIds.Any(id => _values.ContainsKey(id))
        ? ChartValue.Format(_expression.Evaluate(_values))
        : null;

    protected override bool ApplyNumber(string id, double value)
    {
        _values[id] = value;
        return true;
    }
}
