using System.Buffers;
using System.IO.Pipes;
using System.Threading.Channels;
using System.Text;

namespace DevTerm.Core.Sessions;

/// <summary>
/// Read-only tap that publishes one live session's traffic on a named pipe so another process can tail it
/// (<c>devterm-session-{name}</c>, local machine only). Register it with <see cref="Session.AddObserver"/>.
/// Each event is one text line: <c>open</c>, <c>rx &lt;hex&gt;</c>, <c>tx &lt;hex&gt;</c> or <c>closed &lt;reason&gt;</c>.
/// Clients can only read; nothing they write is ever acted on. A slow or vanished client is dropped, never
/// allowed to block the session. See docs/design/proposals/cross-process-control-channel.md.
/// </summary>
public sealed class SessionPipeServer : ISessionObserver, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Channel<string>> _clients = [];
    private readonly List<Task> _writers = [];
    private readonly Lock _gate = new();
    private readonly Task _acceptLoop;

    public SessionPipeServer(string sessionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        PipeName = PipeNameFor(sessionName);
        _acceptLoop = Task.Run(AcceptAsync);
    }

    public string PipeName { get; }

    public static string PipeNameFor(string sessionName) => "devterm-session-" + sessionName;

    public void OnOpened() => Broadcast("open");

    public void OnReceived(ReadOnlySequence<byte> data) => Broadcast("rx " + Convert.ToHexString(data.ToArray()));

    public void OnSent(ReadOnlyMemory<byte> data) => Broadcast("tx " + Convert.ToHexString(data.Span));

    public void OnClosed(bool requested, Exception? error) =>
        Broadcast("closed " + (error?.Message ?? (requested ? "requested" : "peer closed")).ReplaceLineEndings(" "));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        Task[] writers;
        lock (_gate)
        {
            foreach (var client in _clients)
            {
                client.Writer.TryComplete();
            }

            _clients.Clear();
            writers = [.. _writers];
        }

        await Task.WhenAll(writers).ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(PipeName, PipeDirection.Out, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }

            // Bounded and drop-oldest: a slow client loses lines instead of ever blocking the session.
            var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            lock (_gate)
            {
                _clients.Add(queue);
                _writers.Add(Task.Run(() => PumpClientAsync(server, queue)));
            }
        }
    }

    private async Task PumpClientAsync(NamedPipeServerStream server, Channel<string> queue)
    {
        await using (server)
        {
            try
            {
                await using var writer = new StreamWriter(server, new UTF8Encoding(false)) { NewLine = ((char)10).ToString(), AutoFlush = true };
                await foreach (var line in queue.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                // The client went away.
            }
            finally
            {
                lock (_gate)
                {
                    _clients.Remove(queue);
                }
            }
        }
    }

    // Called on the session's read loop: only enqueues, never touches the pipe.
    private void Broadcast(string line)
    {
        lock (_gate)
        {
            foreach (var client in _clients)
            {
                client.Writer.TryWrite(line);
            }
        }
    }
}
