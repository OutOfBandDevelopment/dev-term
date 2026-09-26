namespace DevTerm.Transports.Loopback;

/// <summary>The default script — the example commands this transport was designed around.</summary>
public static class LoopbackScript
{
    /// <summary>The lines <c>help</c>/<c>?</c> print — kept alongside <see cref="Default"/> so the two can't drift apart.</summary>
    public static readonly IReadOnlyList<string> HelpLines =
    [
        "Commands:",
        "  hello                  -> From Loopback test",
        "  Send Stream: N, ascii  -> an N-character deterministic ASCII run",
        "  Send Events: N         -> N separate 'Event 1'..'Event N' lines",
        "  MEAS?                  -> the next simulated sensor sample (A=.. B=.. C=.. X=.. ...)",
        "  Samples: N             -> the next N simulated sensor samples, one per line",
        "  help or ?              -> this list",
    ];

    /// <summary>
    /// A fresh script. The simulated sensor (<c>MEAS?</c>/<c>Samples: N</c>) keeps its own sample
    /// counter per script instance — per connection — so its sequence is deterministic from the
    /// connection's start (see <see cref="LoopbackGenerators.SensorSample"/>), which is what the
    /// bundled "Loopback Sensor Demo" device manifest charts.
    /// </summary>
    public static IReadOnlyList<LoopbackRule> Default()
    {
        var nextSample = 0;
        return
        [
            LoopbackRule.Literal("hello", "From Loopback test"),
            LoopbackRule.Match(@"^Send Stream: (\d+), (\w+)$", m => [LoopbackGenerators.AsciiStream(int.Parse(m.Groups[1].Value))]),
            LoopbackRule.Match(@"^Send Events: (\d+)$", m => LoopbackGenerators.Events(int.Parse(m.Groups[1].Value))),
            LoopbackRule.Match(@"^MEAS\?$", _ => [LoopbackGenerators.SensorSample(nextSample++)]),
            LoopbackRule.Match(@"^Samples: (\d+)$", m =>
            {
                var count = Math.Clamp(int.Parse(m.Groups[1].Value), 0, 1000);
                var first = nextSample;
                nextSample += count;
                return Enumerable.Range(first, count).Select(LoopbackGenerators.SensorSample).ToList();
            }),
            LoopbackRule.Match(@"^(help|\?)$", _ => HelpLines),
        ];
    }
}
