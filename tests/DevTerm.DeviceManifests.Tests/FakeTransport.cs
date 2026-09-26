using System.IO.Pipelines;
using DevTerm.Core.Transports;

namespace DevTerm.DeviceManifests.Tests;

/// <summary>A minimal in-memory <see cref="ITransport"/>: records every write, and pushes "device" bytes back on demand.</summary>
internal sealed class FakeTransport : ITransport
{
    private readonly Pipe _pipe = new();
    private ConnectionState _state = ConnectionState.Closed;

    public List<byte[]> WrittenPayloads { get; } = [];

    public ConnectionState State
    {
        get => _state;
        private set
        {
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

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        WrittenPayloads.Add(data.ToArray());
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    public async Task PushIncomingAsync(byte[] data) => await _pipe.Writer.WriteAsync(data);
}
