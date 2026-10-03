using System.Buffers;
using DevTerm.Core.Presenters;

namespace DevTerm.Core.Plugins;

/// <summary>
/// An approved out-of-process plugin as a presenter the catalog can hold: it only starts the program on first use, so
/// resolving the catalog never spawns a process, and a plugin that won't start renders nothing (see <see cref="FaultReason"/>)
/// instead of failing the app.
/// </summary>
public sealed class LazyExternalPresenter(string name, string fileName, IReadOnlyList<string> arguments, TimeSpan replyTimeout) : IPresenter, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private ExternalProcessPresenter? _inner;
    private bool _started;

    public string Name { get; } = name;

    public bool Faulted => _inner?.Faulted ?? FaultReason is not null;

    public string? FaultReason { get; private set; }

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        lock (_gate)
        {
            if (!_started)
            {
                _started = true;
                try
                {
                    _inner = ExternalProcessPresenter.StartAsync(fileName, arguments, replyTimeout).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException ex)
                {
                    FaultReason = ex.Message;
                }
            }

            return _inner?.Render(data) ?? [];
        }
    }

    public ValueTask DisposeAsync() => _inner?.DisposeAsync() ?? ValueTask.CompletedTask;
}
