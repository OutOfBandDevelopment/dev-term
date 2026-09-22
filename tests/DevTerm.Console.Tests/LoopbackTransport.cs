using System.IO.Pipelines;
using System.Text;
using System.Text.RegularExpressions;
using DevTerm.Core.Transports;

namespace DevTerm.Console.Tests;

/// <summary>
/// A single scripted command for <see cref="LoopbackTransport"/>: a regex matched against the
/// ASCII-decoded, terminator-stripped line a caller wrote, and a generator producing zero or more
/// response lines from that match. Each response line is pushed back as its own write, not
/// concatenated, so a line-buffering presenter (e.g. <c>AsciiPresenter</c>) sees them as separate
/// lines/events rather than one blob.
/// </summary>
internal sealed record LoopbackRule(Regex Pattern, Func<Match, IEnumerable<string>> Respond)
{
    /// <summary>An exact-text command producing one fixed response line, e.g. "hello" -&gt; "From Loopback test".</summary>
    public static LoopbackRule Literal(string command, string response) =>
        new(new Regex($"^{Regex.Escape(command)}$"), _ => [response]);

    /// <summary>A regex command whose capture groups feed the response generator, e.g. "Send Events: (\d+)".</summary>
    public static LoopbackRule Match(string pattern, Func<Match, IEnumerable<string>> respond) =>
        new(new Regex(pattern), respond);
}

/// <summary>
/// Deterministic response generators for <see cref="LoopbackRule"/> — no <see cref="Random"/>, so a
/// given input always produces the same bytes and tests can assert on exact expected output.
/// </summary>
internal static class LoopbackGenerators
{
    /// <summary>A fixed-length, repeating A-Z run — a deterministic stand-in for "N bytes of ascii data".</summary>
    public static string AsciiStream(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

    /// <summary>"Event 1".."Event N", one per response line.</summary>
    public static IEnumerable<string> Events(int count) =>
        Enumerable.Range(1, count).Select(i => $"Event {i}");
}

/// <summary>The default script — the example commands this transport was designed around.</summary>
internal static class LoopbackScript
{
    public static IReadOnlyList<LoopbackRule> Default() =>
    [
        LoopbackRule.Literal("hello", "From Loopback test"),
        LoopbackRule.Match(@"^Send Stream: (\d+), (\w+)$", m => [LoopbackGenerators.AsciiStream(int.Parse(m.Groups[1].Value))]),
        LoopbackRule.Match(@"^Send Events: (\d+)$", m => LoopbackGenerators.Events(int.Parse(m.Groups[1].Value))),
    ];
}

/// <summary>
/// An in-process <see cref="ITransport"/> that answers scripted commands deterministically, instead
/// of a real device or even a real socket — the same <see cref="Pipe"/>-backed shape as
/// <see cref="FakeTransport"/>, but with request/response behavior baked in so a test doesn't have
/// to manually call <c>PushIncomingAsync</c> after every write. Exists to exercise
/// Session/TuiMode/MainWindow logic against realistic multi-line replies and "event stream" style
/// output without any real transport I/O — see docs/design/testing.md.
/// </summary>
internal sealed class LoopbackTransport : ITransport
{
    private readonly Pipe _pipe = new();
    private readonly IReadOnlyList<LoopbackRule> _rules;
    private ConnectionState _state = ConnectionState.Closed;

    /// <param name="rules">The script to answer with — defaults to <see cref="LoopbackScript.Default"/> when omitted.</param>
    public LoopbackTransport(IReadOnlyList<LoopbackRule>? rules = null) => _rules = rules ?? LoopbackScript.Default();

    public List<byte[]> WrittenPayloads { get; } = [];

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            var previous = _state;
            _state = value;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previous, value));
        }
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public PipeReader Input => _pipe.Reader;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _pipe.Writer.CompleteAsync();
        State = ConnectionState.Closed;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        WrittenPayloads.Add(data.ToArray());

        var line = Encoding.ASCII.GetString(data.Span).TrimEnd('\r', '\n');
        if (line.Length == 0)
        {
            return;
        }

        var rule = _rules.FirstOrDefault(r => r.Pattern.IsMatch(line));
        if (rule is null)
        {
            // A visible marker rather than silence: a test whose rules don't cover a sent command
            // should fail loudly (wrong/missing output), not hang waiting on a reply that never comes.
            await PushLineAsync($"? Unrecognized: {line}");
            return;
        }

        foreach (var responseLine in rule.Respond(rule.Pattern.Match(line)))
        {
            await PushLineAsync(responseLine);
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    private async Task PushLineAsync(string text) =>
        await _pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n"));
}
