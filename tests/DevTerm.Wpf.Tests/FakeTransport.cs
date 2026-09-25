using System.IO.Pipelines;
using DevTerm.Core.Transports;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// A minimal, in-memory <see cref="ITransport"/> for driving a real <c>Session</c> (and, through
/// it, a real <see cref="MainWindow"/>) in-process without any real hardware/socket — the WPF
/// equivalent of <c>DevTerm.Console.Tests.FakeTransport</c>. Can also fail on demand (the next write,
/// the read side, or the next open), and reopens with a fresh pipe after a close, so error/reconnect
/// paths can be tested.
/// </summary>
internal sealed class FakeTransport : ITransport
{
    private Pipe _pipe = new();
    private bool _pipeCompleted;
    private ConnectionState _state = ConnectionState.Closed;
    private Exception? _nextWriteFailure;
    private Exception? _nextOpenFailure;

    public List<byte[]> WrittenPayloads { get; } = [];

    public int OpenCount { get; private set; }

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
        if (_nextOpenFailure is { } failure)
        {
            _nextOpenFailure = null;
            State = ConnectionState.Faulted;
            return Task.FromException(failure);
        }

        if (_pipeCompleted)
        {
            _pipe = new Pipe();
            _pipeCompleted = false;
        }

        OpenCount++;
        State = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _pipe.Writer.CompleteAsync();
        _pipeCompleted = true;
        State = ConnectionState.Closed;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_nextWriteFailure is { } failure)
        {
            _nextWriteFailure = null;
            return Task.FromException(failure);
        }

        WrittenPayloads.Add(data.ToArray());
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    /// <summary>Simulates a device sending bytes back, as if they'd arrived over the wire.</summary>
    public async Task PushIncomingAsync(byte[] data) => await _pipe.Writer.WriteAsync(data);

    /// <summary>Makes the next <see cref="WriteAsync"/> fail with <paramref name="failure"/>, as a real device I/O error would.</summary>
    public void FailNextWrite(Exception failure) => _nextWriteFailure = failure;

    /// <summary>Makes the next <see cref="OpenAsync"/> fail with <paramref name="failure"/>.</summary>
    public void FailNextOpen(Exception failure) => _nextOpenFailure = failure;

    /// <summary>Simulates the read side dying (an unplugged cable, a reset socket).</summary>
    public async Task FailReadAsync(Exception failure)
    {
        await _pipe.Writer.CompleteAsync(failure);
        _pipeCompleted = true;
    }
}
