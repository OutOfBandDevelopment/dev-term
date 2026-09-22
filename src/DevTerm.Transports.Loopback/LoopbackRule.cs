using System.Text.RegularExpressions;

namespace DevTerm.Transports.Loopback;

/// <summary>
/// A single scripted command for <see cref="LoopbackTransport"/>: a regex matched against the
/// ASCII-decoded, terminator-stripped line a caller wrote, and a generator producing zero or more
/// response lines from that match. Each response line is pushed back as its own write, not
/// concatenated, so a line-buffering presenter (e.g. <c>AsciiPresenter</c>) sees them as separate
/// lines/events rather than one blob.
/// </summary>
/// <remarks>
/// Mirrors the shape of <c>DevTerm.Console.Tests.LoopbackTransport</c>'s test-only prototype — that
/// one stays internal to its test assembly for scripting test scenarios; this is the real,
/// production-selectable transport a user without hardware can pick from the UI.
/// </remarks>
public sealed record LoopbackRule(Regex Pattern, Func<Match, IEnumerable<string>> Respond)
{
    /// <summary>An exact-text command producing one fixed response line, e.g. "hello" -&gt; "From Loopback test". Case-insensitive.</summary>
    public static LoopbackRule Literal(string command, string response) =>
        new(new Regex($"^{Regex.Escape(command)}$", RegexOptions.IgnoreCase), _ => [response]);

    /// <summary>A regex command whose capture groups feed the response generator, e.g. "Send Events: (\d+)". Case-insensitive.</summary>
    public static LoopbackRule Match(string pattern, Func<Match, IEnumerable<string>> respond) =>
        new(new Regex(pattern, RegexOptions.IgnoreCase), respond);
}
