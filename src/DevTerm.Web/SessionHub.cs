using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;

namespace DevTerm.Web;

/// <summary>
/// The one live <see cref="Session"/> every browser viewer shares. Output is broadcast to all viewers (with a short replayed
/// backlog for late joiners) and sends are serialized, so concurrent senders interleave whole lines, never bytes.
/// </summary>
public sealed class SessionHub : IAsyncDisposable
{
    private readonly Session _session;
    private readonly CliOptions _options;
    private readonly IPresenterInput _input;
    private readonly string _parser;
    private readonly int _backlogLimit;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _gate = new();
    private readonly Queue<string> _backlog = new();

    public SessionHub(Session session, PresenterCatalog catalog, CliOptions options, int backlogLines)
    {
        _session = session;
        Catalog = catalog;
        _options = options;
        _parser = options.EffectiveParser;
        _input = catalog.TryGetInput(_parser, out var input)
            ? input
            : throw new InvalidOperationException($"Unknown parser '{_parser}'. Available: {string.Join(", ", catalog.InputNames)}");
        _backlogLimit = Math.Max(1, backlogLines);
        _session.Output += (_, output) => Publish($"[{output.PresenterName}] {output.Text}");
        _session.Disconnected += (_, e) => Publish($"! {ConnectionErrorMessages.ForDisconnect(options.Transport, e.Error)} The next line sent will reconnect.");
    }

    /// <summary>Raised for every output or status line.</summary>
    public event Action<string>? LineReceived;

    internal Session Session => _session;

    /// <summary>The catalog this session's presenters came from (a fresh catalog would be a different, unconnected set).</summary>
    internal PresenterCatalog Catalog { get; }

    public ConnectionState State => _session.State;

    public string Description => ConnectionDescription.For(_options);

    public IReadOnlyList<string> Backlog
    {
        get
        {
            lock (_gate)
            {
                return [.. _backlog];
            }
        }
    }

    /// <summary>Connects; a failure is shown to viewers instead of thrown, like the TUI/WPF startup, and the next send retries.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _session.OpenAsync(cancellationToken);
            Publish($"Connected to {Description}.");
        }
        catch (Exception ex)
        {
            Publish($"! {ConnectionErrorMessages.For(_options.Transport, ex)}");
        }
    }

    /// <summary>Encodes and sends one typed line. Returns an error message for the sender, or <see langword="null"/>.</summary>
    public async Task<string?> SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length == 0)
        {
            return null;
        }

        if (!TypedInput.TryEncode(_input, _parser, line, _options.LineEnding, out var payload, out var error))
        {
            return error;
        }

        if (payload.Length == 0)
        {
            return null;
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_session.State != ConnectionState.Open)
            {
                try
                {
                    await _session.OpenAsync(cancellationToken);
                    Publish($"Reconnected to {Description}.");
                }
                catch (Exception ex)
                {
                    return $"{ConnectionErrorMessages.For(_options.Transport, ex)} Not sent.";
                }
            }

            try
            {
                await _session.SendAsync(payload, cancellationToken);
            }
            catch (Exception ex) when (_session.State == ConnectionState.Open)
            {
                // A failure that closed the session was already reported through Disconnected.
                return $"Send failed: {ex.Message}";
            }
            catch (Exception)
            {
                return null;
            }
        }
        finally
        {
            _sendLock.Release();
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        _sendLock.Dispose();
    }

    private void Publish(string line)
    {
        lock (_gate)
        {
            _backlog.Enqueue(line);
            while (_backlog.Count > _backlogLimit)
            {
                _backlog.Dequeue();
            }
        }

        LineReceived?.Invoke(line);
    }
}
