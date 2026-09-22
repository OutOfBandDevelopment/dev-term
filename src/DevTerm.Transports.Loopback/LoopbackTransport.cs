using System.IO.Pipelines;
using System.Text;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Loopback;

/// <summary>
/// An in-process <see cref="ITransport"/> that answers scripted commands deterministically, with no
/// real device or even a real socket behind it — so a user with no hardware attached can still pick
/// "loopback" from either front end's transport picker and exercise the UI end-to-end. Purely
/// <see cref="Pipe"/>-backed (no background pump, no cancellation hazard to work around like
/// serial/HID): there's no real <see cref="Stream"/> underneath, just an in-memory script.
/// See docs/design/testing.md.
/// </summary>
public sealed class LoopbackTransport : ITransport
{
    private readonly Pipe _pipe = new();
    private readonly IReadOnlyList<LoopbackRule> _rules;
    private ConnectionState _state = ConnectionState.Closed;

    public LoopbackTransport(IOptions<LoopbackTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Options is reserved for a future custom-script extension; today the transport always
        // answers with the fixed example script.
        _rules = LoopbackScript.Default();
    }

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
        var line = Encoding.ASCII.GetString(data.Span).TrimEnd('\r', '\n');
        if (line.Length == 0)
        {
            return;
        }

        var rule = _rules.FirstOrDefault(r => r.Pattern.IsMatch(line));
        if (rule is null)
        {
            // A visible marker rather than silence: a user's typo or unscripted command should
            // produce visible feedback, not a hang waiting on a reply that never comes.
            await PushLineAsync($"? Unrecognized: {line}");
            return;
        }

        foreach (var responseLine in rule.Respond(rule.Pattern.Match(line)))
        {
            await PushLineAsync(responseLine);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }

    private async Task PushLineAsync(string text) =>
        await _pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n"));
}
