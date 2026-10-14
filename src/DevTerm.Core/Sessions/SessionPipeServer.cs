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

    private const string _pipeRoot = @"\\.\pipe\";

    /// <summary>The names (without the <c>devterm-session-</c> prefix) of the sessions currently publishing on this machine, sorted. Empty off Windows, where pipes are not listed this way.</summary>
    public static IReadOnlyList<string> LiveSessions(string? pipeDirectory = null)
    {
        var directory = pipeDirectory ?? _pipeRoot;
        var prefix = PipeNameFor(string.Empty);
        try
        {
            return Directory.EnumerateFileSystemEntries(directory, prefix + "*")
                .Select(path => Path.GetFileName(path)[prefix.Length..])
                .Where(name => name.Length > 0)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// The pipe name to publish: <paramref name="requested"/>, or <paramref name="fallback"/> (the profile name, else
    /// the transport) when it is the bare <c>true</c> a flag with no name gives, made unique among live sessions.
    /// </summary>
    public static string Resolve(string requested, string fallback)
    {
        var name = string.Equals(requested, "true", StringComparison.OrdinalIgnoreCase) ? fallback : requested;
        return AvailableName(name, LiveSessions());
    }

    /// <summary>
    /// <paramref name="requested"/> when no live session uses it, else <c>name-2</c>, <c>name-3</c>, ... — so a second
    /// process started from the same profile still gets its own pipe instead of failing to bind.
    /// </summary>
    public static string AvailableName(string requested, IReadOnlyCollection<string> live)
    {
        if (!live.Contains(requested, StringComparer.OrdinalIgnoreCase))
        {
            return requested;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{requested}-{n}";
            if (!live.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
    }

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
