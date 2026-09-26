namespace DevTerm.Transports.Loopback;

/// <summary>
/// Deterministic response generators for <see cref="LoopbackRule"/> — no <see cref="Random"/>, so a
/// given input always produces the same bytes and callers can rely on exact expected output.
/// </summary>
public static class LoopbackGenerators
{
    /// <summary>A fixed-length, repeating A-Z run — a deterministic stand-in for "N bytes of ascii data".</summary>
    public static string AsciiStream(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

    /// <summary>"Event 1".."Event N", one per response line.</summary>
    public static IEnumerable<string> Events(int count) =>
        Enumerable.Range(1, count).Select(i => $"Event {i}");

    /// <summary>
    /// Simulated sensor sample number <paramref name="index"/> — a pure function of the index:
    /// three 0–100 channels (<c>A</c> a sine, <c>B</c> a slower cosine, <c>C</c> a sawtooth), an x/y/z
    /// point circling inside ±1 (<c>X</c>, <c>Y</c>, <c>Z</c>), a polar r/theta (<c>R</c> 0–1,
    /// <c>T</c> degrees), and a hue in degrees (<c>H</c>), e.g.
    /// <c>A=50.00 B=90.00 C=0.00 X=0.80 Y=0.00 Z=0.00 R=0.50 T=0.00 H=0.00</c>.
    /// </summary>
    public static string SensorSample(int index)
    {
        var n = (double)index;
        var values = new (string Name, double Value)[]
        {
            ("A", 50 + (40 * Math.Sin(n * 0.3))),
            ("B", 50 + (40 * Math.Cos(n * 0.13))),
            ("C", index * 7 % 100),
            ("X", 0.8 * Math.Cos(n * 0.4)),
            ("Y", 0.8 * Math.Sin(n * 0.4)),
            ("Z", Math.Sin(n * 0.1)),
            ("R", 0.5 + (0.4 * Math.Sin(n * 0.25))),
            ("T", index * 15 % 360),
            ("H", index * 10 % 360),
        };
        return string.Join(' ', values.Select(v => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{v.Name}={v.Value:0.00}")));
    }
}
