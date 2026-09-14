using System.Net.Sockets;

namespace DevTerm.Transports.Tcp;

/// <summary>
/// <see cref="ITcpConnection"/> backed by a real <see cref="TcpClient"/>. Deliberately thin —
/// logic worth unit testing belongs in <see cref="TcpTransport"/>, which depends on the
/// interface instead of this class.
/// </summary>
public sealed class SystemTcpConnection : ITcpConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _readLoopCts = new();
    private readonly Task _readLoop;

    public SystemTcpConnection(TcpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _stream = client.GetStream();
        _readLoop = Task.Run(() => ReadLoopAsync(_readLoopCts.Token));
    }

    public event EventHandler<TcpDataReceivedEventArgs>? DataReceived;

    public event EventHandler? Closed;

    public void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                DataReceived?.Invoke(this, new TcpDataReceivedEventArgs(buffer[..read]));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException)
        {
            // Connection reset/aborted by the remote peer; fall through to Closed below.
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _readLoopCts.Cancel();
        _stream.Dispose();
        _client.Dispose();
        _readLoopCts.Dispose();
    }
}
