using System.Buffers;
using System.Threading.Channels;
using DevTerm.Core.Presenters;

namespace DevTerm.Core.Plugins;

/// <summary>
/// An approved out-of-process plugin as a presenter the catalog can hold: it only starts the program on first use, so
/// resolving the catalog never spawns a process, and a plugin that won't start renders nothing (see <see cref="FaultReason"/>)
/// instead of failing the app. <see cref="Render"/> never waits for the plugin: chunks are queued to one worker that
/// starts the program, asks it, and delivers the lines through <see cref="OutputReady"/>, so the read loop and the
/// other presenters are never held up by a slow plugin (decided 2026-10-13).
/// </summary>
public sealed class LazyExternalPresenter(string name, string fileName, IReadOnlyList<string> arguments, TimeSpan replyTimeout)
    : IPresenter, IAsyncOutputSource, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Channel<byte[]> _queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private Task? _worker;
    private ExternalProcessPresenter? _inner;
    private string? _faultReason;

    public event EventHandler<IReadOnlyList<string>>? OutputReady;

    public string Name { get; } = name;

    public bool Faulted => (_inner?.Faulted ?? false) || FaultReason is not null;

    public string? FaultReason => _faultReason ?? _inner?.FaultReason;

    /// <summary>Completes when everything queued so far has been answered (or dropped); for tests and shutdown.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var marker = new TaskCompletionSource();
        lock (_gate)
        {
            _flushMarkers.Enqueue(marker);
        }

        _queue.Writer.TryWrite([]);
        await marker.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private readonly Queue<TaskCompletionSource> _flushMarkers = new();

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        if (Faulted)
        {
            return [];
        }

        lock (_gate)
        {
            _worker ??= Task.Run(RunAsync);
        }

        _queue.Writer.TryWrite(data.ToArray());
        return [];
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        Task? worker;
        lock (_gate)
        {
            worker = _worker;
        }

        if (worker is not null)
        {
            try
            {
                await worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
        }

        if (_inner is not null)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunAsync()
    {
        await foreach (var chunk in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (chunk.Length == 0)
            {
                // A flush marker.
                TaskCompletionSource? marker;
                lock (_gate)
                {
                    marker = _flushMarkers.Count > 0 ? _flushMarkers.Dequeue() : null;
                }

                marker?.TrySetResult();
                continue;
            }

            if (_inner is null && _faultReason is null)
            {
                try
                {
                    _inner = await ExternalProcessPresenter.StartAsync(fileName, arguments, replyTimeout).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    _faultReason = ex.Message;
                }
            }

            if (_inner is null)
            {
                continue;
            }

            var lines = _inner.Render(new ReadOnlySequence<byte>(chunk));
            if (lines.Count > 0)
            {
                OutputReady?.Invoke(this, lines);
            }
        }
    }
}
