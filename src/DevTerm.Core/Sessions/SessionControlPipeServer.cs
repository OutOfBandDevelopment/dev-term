using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

namespace DevTerm.Core.Sessions;

/// <summary>
/// Read-write control channel for one live session on a named pipe (<c>devterm-control-{name}</c>), restricted to the
/// current OS user. Register it with <see cref="Session.AddObserver"/> to also stream the same event lines as
/// <see cref="SessionPipeServer"/> (<c>open</c>, <c>rx HEX</c>, <c>tx HEX</c>, <c>closed ...</c>).
/// A client may write one command per line and gets one reply line each: <c>ok</c> or <c>error &lt;why&gt;</c>.
/// Commands: <c>send &lt;text&gt;</c> (encoded by the host's typed-input rules), <c>sendhex &lt;HEX&gt;</c> (raw bytes), <c>ping</c>.
/// Sends go through <see cref="Session.SendAsync"/>, so they interleave safely with the front end's own typing.
/// See docs/design/proposals/cross-process-control-channel.md.
/// </summary>
public sealed class SessionControlPipeServer : ISessionObserver, IAsyncDisposable
{
    private readonly Session _session;
    private readonly Func<string, (byte[]? Payload, string? Error)> _encodeText;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Channel<string>> _clients = [];
    private readonly List<Task> _handlers = [];
    private readonly Lock _gate = new();
    private readonly Task _acceptLoop;

    /// <param name="encodeText">Turns a <c>send</c> command's text into wire bytes (the host's parser and line ending), or an error message.</param>
    public SessionControlPipeServer(Session session, string sessionName, Func<string, (byte[]? Payload, string? Error)> encodeText)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        _session = session;
        _encodeText = encodeText ?? throw new ArgumentNullException(nameof(encodeText));
        PipeName = PipeNameFor(sessionName);
        _acceptLoop = Task.Run(AcceptAsync);
    }

    public string PipeName { get; }

    public static string PipeNameFor(string sessionName) => "devterm-control-" + sessionName;

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

        Task[] handlers;
        lock (_gate)
        {
            foreach (var client in _clients)
            {
                client.Writer.TryComplete();
            }

            handlers = [.. _handlers];
        }

        await Task.WhenAll(handlers).ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }

            var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            lock (_gate)
            {
                _clients.Add(queue);
                _handlers.Add(Task.Run(() => HandleClientAsync(server, queue)));
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, Channel<string> queue)
    {
        await using (server)
        {
            try
            {
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { NewLine = ((char)10).ToString(), AutoFlush = true };
                var pump = Task.Run(async () =>
                {
                    await foreach (var line in queue.Reader.ReadAllAsync().ConfigureAwait(false))
                    {
                        await writer.WriteLineAsync(line).ConfigureAwait(false);
                    }
                });

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                while (await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false) is { } command)
                {
                    queue.Writer.TryWrite(await SessionCommands.ExecuteAsync(_session, _encodeText, command, _stop.Token).ConfigureAwait(false));
                }

                queue.Writer.TryComplete();
                await pump.ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The client went away, or the server is shutting down.
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
