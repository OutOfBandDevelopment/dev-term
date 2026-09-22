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
}
