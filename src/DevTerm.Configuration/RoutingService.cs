using DevTerm.Core.Routing;
using DevTerm.Core.Sessions;

namespace DevTerm.Configuration;

public enum RoutingState
{
    Stopped,
    Connecting,
    Connected,

    /// <summary>The broker is unreachable or was lost; retrying with backoff. <see cref="RoutingService.Reason"/> says why.</summary>
    Reconnecting,

    /// <summary>The configuration is invalid; routing did not start. <see cref="RoutingService.Reason"/> says why.</summary>
    Failed,
}

/// <summary>
/// Runs one profile's routing on a live <see cref="Session"/>: adds a <see cref="MessageRouter"/> to it, connects the broker in the
/// background (never blocking or failing the device connection), and reconnects with backoff after a loss. Device lines published
/// while the broker is down are dropped and counted (<see cref="DroppedPublishes"/>). See docs/specs/routing-window.md.
/// </summary>
public sealed class RoutingService : IAsyncDisposable
{
    private readonly RoutingOptions _options;
    private readonly IRoutingLinkFactory _factory;
    private readonly Lock _gate = new();
    private Session? _session;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IRoutingLink? _link;
    private int _dropped;

    public RoutingService(RoutingOptions options, IRoutingLinkFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _factory = factory ?? new RoutingLinkFactory();
    }

    /// <summary>Waits between reconnect attempts: the n-th retry waits the n-th entry, then repeats the last.</summary>
    public IReadOnlyList<TimeSpan> Backoff { get; set; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public RoutingState State { get; private set; } = RoutingState.Stopped;

    /// <summary>The last failure while <see cref="State"/> is Reconnecting or Failed.</summary>
    public string? Reason { get; private set; }

    /// <summary>The live router while routing runs (its history, hit counts and unmatched count feed the window); null when stopped.</summary>
    public MessageRouter? Router { get; private set; }

    /// <summary>Device-to-broker messages dropped because the broker was not connected.</summary>
    public int DroppedPublishes => Volatile.Read(ref _dropped);

    public event EventHandler? StateChanged;

    /// <summary>Starts routing on <paramref name="session"/>; returns at once (the broker connects in the background). A bad configuration ends in <see cref="RoutingState.Failed"/>, not an exception.</summary>
    public void Start(Session session, string terminator = "\r\n", Func<RoutingRule, string, Task<RoutingConfirmChoice>>? confirm = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            if (State is not (RoutingState.Stopped or RoutingState.Failed))
            {
                return;
            }

            if (_options.Validate() is { } problem)
            {
                Set(RoutingState.Failed, problem);
                return;
            }

            var router = new MessageRouter(new RoutingRuleSet { Rules = _options.Rules }, new LinkSink(this), terminator: terminator) { Confirm = confirm };
            _session = session;
            Router = router;
            session.AddPresenter(router);
            _cts = new CancellationTokenSource();
            _dropped = 0;
            Set(RoutingState.Connecting, null);
            _loop = Task.Run(() => RunAsync(router, _cts.Token), CancellationToken.None);
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        IRoutingLink? link;
        lock (_gate)
        {
            if (State == RoutingState.Stopped)
            {
                return;
            }

            _cts?.Cancel();
            loop = _loop;
            link = _link;
            _link = null;
            if (_session is not null && Router is not null)
            {
                _session.RemovePresenter(Router);
            }

            _session = null;
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: Stop cancels the connect loop.
            }
        }

        if (link is not null)
        {
            await link.DisposeAsync().ConfigureAwait(false);
        }

        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
            _loop = null;
            Set(RoutingState.Stopped, null);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(MessageRouter router, CancellationToken ct)
    {
        for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var link = _factory.Create(_options);
            link.Lost += error => lost.TrySetResult();
            try
            {
                await link.StartAsync(router, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await link.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                await link.DisposeAsync().ConfigureAwait(false);
                Set(RoutingState.Reconnecting, ex.Message);
                await DelayAsync(attempt, ct).ConfigureAwait(false);
                continue;
            }

            lock (_gate)
            {
                if (ct.IsCancellationRequested)
                {
                    _ = link.DisposeAsync();
                    return;
                }

                _link = link;
                Set(RoutingState.Connected, null);
            }

            attempt = -1;
            using (ct.Register(() => lost.TrySetCanceled(ct)))
            {
                try
                {
                    await lost.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            lock (_gate)
            {
                _link = null;
                Set(RoutingState.Reconnecting, "The broker connection was lost.");
            }

            await link.DisposeAsync().ConfigureAwait(false);
            await DelayAsync(0, ct).ConfigureAwait(false);
        }
    }

    private async Task DelayAsync(int attempt, CancellationToken ct)
    {
        var wait = Backoff.Count == 0 ? TimeSpan.Zero : Backoff[Math.Min(attempt, Backoff.Count - 1)];
        try
        {
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping: the loop condition ends the run.
        }
    }

    private void Set(RoutingState state, string? reason)
    {
        State = state;
        Reason = reason;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class LinkSink(RoutingService owner) : IMessageSink
    {
        public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            IRoutingLink? link;
            lock (owner._gate)
            {
                link = owner._link;
            }

            if (link is null)
            {
                Interlocked.Increment(ref owner._dropped);
                return;
            }

            await link.PublishAsync(topic, payload, cancellationToken).ConfigureAwait(false);
        }
    }
}
