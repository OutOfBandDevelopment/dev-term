using System.Buffers;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;

namespace DevTerm.Devices.ZoomH4n;

/// <summary>
/// <see cref="IControlSurface"/> for the Zoom H4n's RC04/RC2 remote protocol, per
/// docs/design/proposals/zoom-h4n-remote-protocol.md. Each button sends a fixed 2-byte press code
/// followed by the shared 2-byte release code (<c>0x80 0x00</c>) as two separate writes — mirroring
/// a physical remote's press-then-release, not one 4-byte frame. The command id is validated
/// against the known button table *before* the handshake runs, so an unknown command id fails fast
/// (<see cref="ArgumentException"/>) rather than paying the handshake's worst-case ~30 second delay
/// for nothing.
///
/// The device needs a one-time init handshake before it responds to anything: repeatedly send
/// <c>0x00</c> (up to 1024 times, ~30ms apart) until a reply byte with the high bit set arrives,
/// then send the fixed 3-byte wake sequence <c>0xA1 0x80 0x00</c>. This runs lazily on first
/// <see cref="InvokeAsync"/>, guarded by a semaphore so concurrent first calls only run it once.
/// Detecting the high-bit reply byte needs to see every raw byte the device sends regardless of
/// which presenter the user has selected for display, so a private, non-DI-registered
/// <see cref="ZoomH4nWakeWatcher"/> is bound directly into the session's live pipeline via
/// <see cref="Session.AddPresenter"/> from this surface's own constructor — unlike
/// <c>DevTerm.Devices.K8055</c>/Busylight/RadexOne's live indicators, which only update if the user
/// separately selected that device's presenter for display.
///
/// Also an <see cref="ICommandPreview"/>: <see cref="PreviewCommand"/> returns the exact press+release
/// bytes a button would send, computed directly from the static frame table with no I/O and no
/// handshake side effect (an unknown command id returns null).
///
/// <see cref="Dispose"/> unbinds the wake watcher again (mirroring
/// <see cref="DevTerm.DeviceManifests.ManifestPanel"/>'s own Attach/Dispose pattern for its reply
/// presenter) so a front end's panel-closing code can remove it from the session's live pipeline —
/// without this, reopening the panel stacks up another watcher scanning every received byte for the
/// rest of the session (see docs/bugs/020-zoomh4n-wake-watcher-leak.md).
/// </summary>
public sealed class ZoomH4nControlSurface : IControlSurface, ICommandPreview, IDisposable
{
    private static readonly byte[] _releaseCode = [0x80, 0x00];

    private static readonly Dictionary<string, byte[]> _pressCodes = new(StringComparer.Ordinal)
    {
        ["record"] = [0x81, 0x00],
        ["play"] = [0x82, 0x00],
        ["stop"] = [0x84, 0x00],
        ["ffwd"] = [0x88, 0x00],
        ["rwd"] = [0x90, 0x00],
        ["volUp"] = [0x80, 0x08],
        ["volDown"] = [0x80, 0x10],
        ["recUp"] = [0x80, 0x20],
        ["recDown"] = [0x80, 0x40],
        ["mic"] = [0x80, 0x01],
        ["ch1"] = [0x80, 0x02],
        ["ch2"] = [0x80, 0x04],
    };

    private const int _maxHandshakeAttempts = 1024;
    private const int _handshakeAttemptTimeoutMs = 30;

    private readonly Session _session;
    private readonly ZoomH4nWakeWatcher _wakeWatcher = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public ZoomH4nControlSurface(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _session.AddPresenter(_wakeWatcher);
    }

    /// <summary>Unbinds the wake watcher from the session (the panel has closed).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.RemovePresenter(_wakeWatcher);
        _initLock.Dispose();
    }

    public async Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (!_pressCodes.TryGetValue(commandId, out var pressCode))
        {
            throw new ArgumentException($"Unknown Zoom H4n command '{commandId}'.", nameof(commandId));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _session.SendAsync(pressCode, cancellationToken).ConfigureAwait(false);
        await _session.SendAsync(_releaseCode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The press+release bytes invoking <paramref name="commandId"/> would send, as hex; null for an unknown command id. Never triggers the handshake or sends anything.</summary>
    public string? PreviewCommand(string commandId, string? value)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (!_pressCodes.TryGetValue(commandId, out var pressCode))
        {
            return null;
        }

        return CommandPreviewFormat.ToHex([.. pressCode, .. _releaseCode]);
    }

    /// <summary>
    /// Runs the init handshake exactly once. Best-effort: if no high-bit reply byte arrives within
    /// <see cref="_maxHandshakeAttempts"/> attempts, proceeds to send the wake sequence anyway rather
    /// than blocking button presses forever — a device that's already awake (or an adapter that
    /// swallowed the probe bytes) still gets a reasonable chance to respond to real commands.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            for (var attempt = 0; attempt < _maxHandshakeAttempts; attempt++)
            {
                var wakeByteTask = _wakeWatcher.WaitForWakeByteAsync();
                await _session.SendAsync(new byte[] { 0x00 }, cancellationToken).ConfigureAwait(false);

                var delayTask = Task.Delay(_handshakeAttemptTimeoutMs, cancellationToken);
                var completed = await Task.WhenAny(wakeByteTask, delayTask).ConfigureAwait(false);
                if (completed == wakeByteTask)
                {
                    break;
                }
            }

            await _session.SendAsync(new byte[] { 0xA1, 0x80, 0x00 }, cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// A private, non-DI-registered <see cref="IPresenter"/> that only exists to let
    /// <see cref="EnsureInitializedAsync"/> await the handshake's high-bit reply byte — it renders
    /// nothing and is never resolvable via <c>PresenterCatalog</c>.
    /// </summary>
    private sealed class ZoomH4nWakeWatcher : IPresenter
    {
        private readonly Lock _lock = new();
        private TaskCompletionSource<bool>? _pending;

        public string Name => "zoomh4n-wake-watcher";

        public Task WaitForWakeByteAsync()
        {
            lock (_lock)
            {
                _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _pending.Task;
            }
        }

        public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
        {
            TaskCompletionSource<bool>? toSignal = null;
            lock (_lock)
            {
                if (_pending is not null)
                {
                    foreach (var segment in data)
                    {
                        foreach (var b in segment.Span)
                        {
                            if ((b & 0x80) != 0)
                            {
                                toSignal = _pending;
                                _pending = null;
                                break;
                            }
                        }

                        if (toSignal is not null)
                        {
                            break;
                        }
                    }
                }
            }

            toSignal?.TrySetResult(true);
            return [];
        }
    }
}
