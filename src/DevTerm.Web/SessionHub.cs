using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Logging;

namespace DevTerm.Web;

/// <summary>
/// The one live <see cref="Session"/> every browser viewer shares. Output is broadcast to all viewers (with a short replayed
/// backlog for late joiners) and sends are serialized, so concurrent senders interleave whole lines, never bytes.
/// </summary>
public sealed class SessionHub : IAsyncDisposable
{
    private Session _session;
    private CliOptions _options;
    private IPresenterInput _input;
    private string _parser;
    private readonly int _backlogLimit;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _gate = new();
    private readonly Queue<string> _backlog = new();

    /// <summary>The options the current session was built from (they change on a profile switch).</summary>
    public CliOptions Options => _options;

    public SessionHub(Session session, PresenterCatalog catalog, CliOptions options, int backlogLines)
    {
        _backlogLimit = Math.Max(1, backlogLines);
        _session = session;
        _options = options;
        Catalog = catalog;
        (_parser, _input) = ResolveInput(catalog, options);
        Attach(session, options);
    }

    private static (string Parser, IPresenterInput Input) ResolveInput(PresenterCatalog catalog, CliOptions options)
    {
        var parser = options.EffectiveParser;
        return catalog.TryGetInput(parser, out var input)
            ? (parser, input)
            : throw new InvalidOperationException($"Unknown parser '{parser}'. Available: {string.Join(", ", catalog.InputNames)}");
    }

    private void Attach(Session session, CliOptions options)
    {
        session.Output += (_, output) => { if (ReferenceEquals(session, _session)) { Publish($"[{output.PresenterName}] {output.Text}"); } };
        session.Disconnected += (_, e) =>
        {
            if (ReferenceEquals(session, _session))
            {
                StateChanged?.Invoke();
                Publish($"! {ConnectionErrorMessages.ForDisconnect(options.Transport, e.Error)} The next line sent will reconnect.");
            }
        };
    }

    /// <summary>
    /// Replaces the host's own connection with <paramref name="options"/> (a saved profile): the old session is closed and
    /// disposed, a new one is built and connected, and viewers stay attached with their output kept. A failed connect is
    /// reported to viewers, and the new profile stays selected (the next sent line retries), as at startup.
    /// </summary>
    public async Task SwitchAsync(CliOptions options, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var built = DevTermSessionBuilder.Build(options);
        var parsed = ResolveInput(built.Catalog, options);
        await _sendLock.WaitAsync(cancellationToken);
        Session old;
        try
        {
            old = _session;
            _session = built.Session;
            _options = options;
            Catalog = built.Catalog;
            (_parser, _input) = parsed;
            Configured = true;
            ProfileName = name;
            Attach(built.Session, options);
        }
        finally
        {
            _sendLock.Release();
        }

        StopLogging();
        await old.DisposeAsync();
        Publish($"Switched to {name}.");
        SessionChanged?.Invoke();
        StateChanged?.Invoke();
        await StartAsync(cancellationToken);
    }

    /// <summary>The saved profile the host's own session was switched to; <see langword="null"/> until a switch.</summary>
    public string? ProfileName { get; private set; }

    /// <summary>The send formats (parsers) a typed line can be encoded with, as the desktop apps' "Send as" list.</summary>
    public IReadOnlyList<string> ParserNames => [.. Catalog.InputNames];

    /// <summary>The send format currently used for typed lines.</summary>
    public string Parser => _parser;

    /// <summary>Changes the send format; false when no such format exists.</summary>
    public bool SetParser(string name)
    {
        if (!Catalog.TryGetInput(name, out var input))
        {
            return false;
        }

        (_parser, _input) = (name, input);
        return true;
    }

    private SessionLogger? _logger;

    /// <summary>The file the session is being logged to, or <see langword="null"/> when not logging.</summary>
    public string? LogPath => _logger?.Path;

    /// <summary>Starts logging this session to <paramref name="path"/> (or the default <c>~/.dev-term/logs</c> name), replacing a running log. Returns the path.</summary>
    public string StartLogging(string? path = null)
    {
        StopLogging();
        var target = path ?? SessionLogging.DefaultLogPath(_options, ProfileName, DateTimeOffset.Now);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        _logger = SessionLogging.Start(target, _session, _options, _parser, ProfileName, "web");
        Publish($"Logging to {SessionLogging.DisplayPath(_logger.Path!)}.");
        return _logger.Path!;
    }

    /// <summary>Stops logging; false when it was not running.</summary>
    public bool StopLogging()
    {
        if (_logger is null)
        {
            return false;
        }

        var path = _logger.Path;
        _logger.Dispose();
        _logger = null;
        Publish($"Stopped logging to {SessionLogging.DisplayPath(path!)}.");
        return true;
    }

    /// <summary>Raised after <see cref="SwitchAsync"/> replaced the session (anything holding the old one must rebind).</summary>
    public event Action? SessionChanged;

    /// <summary>
    /// False when the host was started with no connection to make (nothing in the arguments or the saved default profile);
    /// the main session then stays closed until someone connects it, instead of failing on default serial options.
    /// </summary>
    public bool Configured { get; private set; } = true;

    internal SessionHub WithConfigured(bool configured)
    {
        Configured = configured;
        return this;
    }

    /// <summary>Raised when the session connects, disconnects or is lost, so a page can refresh its button.</summary>
    public event Action? StateChanged;

    /// <summary>Raised for every output or status line.</summary>
    public event Action<string>? LineReceived;

    internal Session Session => _session;

    /// <summary>The catalog this session's presenters came from (a fresh catalog would be a different, unconnected set).</summary>
    internal PresenterCatalog Catalog { get; private set; }

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

    /// <summary>Closes the session on request (the next sent line reconnects, as after a lost connection).</summary>
    public async Task DisconnectAsync()
    {
        if (_session.State == ConnectionState.Open)
        {
            await _session.CloseAsync();
            Publish($"Disconnected from {Description}.");
        }

        StateChanged?.Invoke();
    }

    /// <summary>Connects; a failure is shown to viewers instead of thrown, like the TUI/WPF startup, and the next send retries.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!Configured)
        {
            Publish("Not connected: no connection is configured. Open a saved profile from the picker above, or save one on the Profiles page.");
            return;
        }

        if (_session.State == ConnectionState.Open)
        {
            return;
        }

        try
        {
            await _session.OpenAsync(cancellationToken);
            Publish($"Connected to {Description}.");
        }
        catch (Exception ex)
        {
            Publish($"! {ConnectionErrorMessages.For(_options.Transport, ex)}");
        }

        StateChanged?.Invoke();
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

        if (!Configured)
        {
            return "Not connected: no connection is configured. Open a saved profile from the picker above.";
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
                    StateChanged?.Invoke();
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
        _logger?.Dispose();
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
