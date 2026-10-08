using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace DevTerm.Core.Sessions;

/// <summary>
/// Shares one live session on a TCP port as a transparent byte proxy: everything the device sends is copied to the
/// connected client, and everything the client sends is written to the device through the session. One client at a
/// time (a second connection is closed at once). Binds loopback unless told otherwise, because there is no
/// authentication or encryption. Register it with <see cref="Session.AddObserver"/>. A slow client loses data
/// instead of ever blocking the session. See docs/design/rfc2217.md ("Server mode").
/// </summary>
public sealed class SessionTcpShareServer : ISessionObserver, IAsyncDisposable
{
    private readonly Session _session;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly Task _acceptLoop;
    private Channel<byte[]>? _client;

    public SessionTcpShareServer(Session session, IPAddress bindAddress, int port)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bindAddress);
        _session = session;
        _listener = new TcpListener(bindAddress, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptAsync);
    }

    /// <summary>The port actually listening (useful when 0 was requested).</summary>
    public int Port { get; }

    public void OnOpened()
    {
    }

    public void OnReceived(ReadOnlySequence<byte> data)
    {
        lock (_gate)
        {
            _client?.Writer.TryWrite(data.ToArray());
        }
    }

    public void OnSent(ReadOnlyMemory<byte> data)
    {
    }

    public void OnClosed(bool requested, Exception? error)
    {
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            Channel<byte[]>? queue = null;
            lock (_gate)
            {
                if (_client is null)
                {
                    queue = _client = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
                }
            }

            if (queue is null)
            {
                client.Dispose();
                continue;
            }

            // Serve this client without blocking the next accept (which rejects while one is connected).
            _ = Task.Run(() => ServeAsync(client, queue));
        }
    }

    private async Task ServeAsync(TcpClient client, Channel<byte[]> queue)
    {
        using (client)
        {
            var stream = client.GetStream();
            var writer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var chunk in queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                    {
                        await stream.WriteAsync(chunk, _stop.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                }
            });

            try
            {
                var buffer = new byte[4096];
                int read;
                while ((read = await stream.ReadAsync(buffer, _stop.Token).ConfigureAwait(false)) > 0)
                {
                    await _session.SendAsync(buffer.AsMemory(0, read), _stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // The client went away, or the device link failed; the session reports its own failures.
            }
            finally
            {
                lock (_gate)
                {
                    _client = null;
                }

                queue.Writer.TryComplete();
                client.Dispose();
                await writer.ConfigureAwait(false);
            }
        }
    }
}
