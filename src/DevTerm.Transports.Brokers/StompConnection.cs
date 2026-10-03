using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace DevTerm.Transports.Brokers;

/// <summary>
/// A minimal STOMP 1.2 client over TCP (there is no maintained .NET STOMP library worth the dependency): CONNECT, SUBSCRIBE
/// (auto ack), SEND, DISCONNECT, with MESSAGE and ERROR frames read back. Heart-beating is not negotiated (<c>0,0</c>).
/// </summary>
internal sealed class StompConnection : IBrokerConnection
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _readCts = new();
    private TcpClient? _client;
    private Stream? _stream;
    private Task? _readTask;
    private int _subscriptionId;
    private bool _closing;

    public event Action<string, byte[]>? MessageReceived;

    public event Action<Exception?>? Disconnected;

    public async Task ConnectAsync(BrokerTransportOptions options, CancellationToken cancellationToken)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(options.Host, options.Port, cancellationToken);
        _stream = _client.GetStream();
        if (options.UseTls)
        {
            var tls = new SslStream(_stream, leaveInnerStreamOpen: false, BrokerTls.Validator(options));
            _stream = tls;
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = options.Host }, cancellationToken);
        }

        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), CancellationToken.None);

        var headers = new List<KeyValuePair<string, string>>
        {
            new("accept-version", "1.2"),
            new("host", string.IsNullOrWhiteSpace(options.VirtualHost) ? "/" : options.VirtualHost),
            new("heart-beat", "0,0"),
        };
        if (!string.IsNullOrEmpty(options.Username))
        {
            headers.Add(new("login", options.Username));
            headers.Add(new("passcode", options.Password ?? string.Empty));
        }

        await SendFrameAsync(new StompFrame("CONNECT", headers, []), cancellationToken);
        await _connected.Task.WaitAsync(cancellationToken);
    }

    public Task SubscribeAsync(string address, CancellationToken cancellationToken) =>
        SendFrameAsync(
            new StompFrame("SUBSCRIBE", [new("id", Interlocked.Increment(ref _subscriptionId).ToString(CultureInfo.InvariantCulture)), new("destination", address), new("ack", "auto")], []),
            cancellationToken);

    public Task PublishAsync(string address, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var body = payload.ToArray();
        return SendFrameAsync(
            new StompFrame("SEND", [new("destination", address), new("content-type", "text/plain"), new("content-length", body.Length.ToString(CultureInfo.InvariantCulture))], body),
            cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _closing = true;
        if (_stream is not null)
        {
            try
            {
                await SendFrameAsync(new StompFrame("DISCONNECT", [], []), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                // The broker is already gone.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _closing = true;
        await _readCts.CancelAsync();
        _client?.Dispose();
        if (_readTask is not null)
        {
            await _readTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _readCts.Dispose();
        _writeLock.Dispose();
    }

    private async Task SendFrameAsync(StompFrame frame, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("The STOMP connection is not open.");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(frame.Encode(), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var stream = _stream!;
        var buffer = new List<byte>();
        var chunk = new byte[4096];
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                buffer.AddRange(chunk.AsSpan(0, read).ToArray());
                while (StompFrame.TryParse(CollectionsMarshal.AsSpan(buffer), out var consumed) is { } frame)
                {
                    buffer.RemoveRange(0, consumed);
                    Handle(frame);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            failure = cancellationToken.IsCancellationRequested ? null : ex;
        }

        _connected.TrySetException(failure ?? new IOException("The STOMP broker closed the connection."));
        if (!_closing && !cancellationToken.IsCancellationRequested)
        {
            Disconnected?.Invoke(failure);
        }
    }

    private void Handle(StompFrame frame)
    {
        switch (frame.Command)
        {
            case "CONNECTED":
                _connected.TrySetResult();
                break;
            case "MESSAGE":
                MessageReceived?.Invoke(frame.Header("destination") ?? string.Empty, frame.Body);
                break;
            case "ERROR":
                var error = new IOException("STOMP broker error: " + (frame.Header("message") ?? Encoding.UTF8.GetString(frame.Body)));
                _connected.TrySetException(error);
                if (!_closing)
                {
                    Disconnected?.Invoke(error);
                }

                break;
        }
    }
}

public sealed class StompConnectionFactory : IBrokerConnectionFactory
{
    public IBrokerConnection Create() => new StompConnection();
}
