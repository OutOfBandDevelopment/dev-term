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
        "  help or ?              -> this list",
    ];

    public static IReadOnlyList<LoopbackRule> Default() =>
    [
        LoopbackRule.Literal("hello", "From Loopback test"),
        LoopbackRule.Match(@"^Send Stream: (\d+), (\w+)$", m => [LoopbackGenerators.AsciiStream(int.Parse(m.Groups[1].Value))]),
        LoopbackRule.Match(@"^Send Events: (\d+)$", m => LoopbackGenerators.Events(int.Parse(m.Groups[1].Value))),
        LoopbackRule.Match(@"^(help|\?)$", _ => HelpLines),
    ];
}
