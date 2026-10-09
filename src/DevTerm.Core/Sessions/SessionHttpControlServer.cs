using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace DevTerm.Core.Sessions;

/// <summary>
/// Read-write control of one live session over HTTP on <c>127.0.0.1</c> only, for browsers and non-.NET tools
/// (the localhost web-service twin of <see cref="SessionControlPipeServer"/>). Every request needs the per-run bearer
/// <see cref="Token"/> (<c>Authorization: Bearer ...</c>, or <c>?token=</c> for a browser EventSource), so another local
/// user or a web page cannot drive the device without it. <c>GET /ping</c>; <c>POST /command</c> with a command line as
/// the body (<c>send ...</c>, <c>sendhex ...</c>, <c>ping</c>) answers <c>200 ok</c> or <c>400 error ...</c>;
/// <c>GET /events</c> is a Server-Sent Events stream of the same <c>open</c>/<c>rx</c>/<c>tx</c>/<c>closed</c> lines the pipes carry.
/// Register it with <see cref="Session.AddObserver"/>. See docs/design/proposals/cross-process-control-channel.md.
/// </summary>
public sealed class SessionHttpControlServer : ISessionObserver, IAsyncDisposable
{
    private volatile Session _session;
    private volatile Func<string, (byte[]? Payload, string? Error)> _encodeText;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Channel<string>> _clients = [];
    private readonly Lock _gate = new();
    private readonly Task _loop;

    /// <param name="token">A fixed token to require, or null for a random one.</param>
    /// <param name="port">The loopback port to listen on; 0 picks a free one (see <see cref="Port"/>).</param>
    public SessionHttpControlServer(Session session, int port, Func<string, (byte[]? Payload, string? Error)> encodeText, string? token = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _encodeText = encodeText ?? throw new ArgumentNullException(nameof(encodeText));
        Token = string.IsNullOrWhiteSpace(token) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) : token;
        Port = port == 0 ? FreePort() : port;
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _loop = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>
    /// Points the server at a different session (a front end swapped its connection in place): commands now go to it. The caller
    /// moves the observer registration itself (<see cref="Session.AddObserver"/>); connected event clients are told <c>closed</c>-free,
    /// so they just see the new session's <c>open</c>/<c>rx</c> lines.
    /// </summary>
    public void Rebind(Session session, Func<string, (byte[]? Payload, string? Error)> encodeText)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(encodeText);
        _session = session;
        _encodeText = encodeText;
    }

    /// <summary>The bearer token every request must present; random per run unless one was supplied.</summary>
    public string Token { get; }

    public void OnOpened() => Broadcast("open");

    public void OnReceived(ReadOnlySequence<byte> data) => Broadcast("rx " + Convert.ToHexString(data.ToArray()));

    public void OnSent(ReadOnlyMemory<byte> data) => Broadcast("tx " + Convert.ToHexString(data.Span));

    public void OnClosed(bool requested, Exception? error) =>
        Broadcast("closed " + (error?.Message ?? (requested ? "requested" : "peer closed")).ReplaceLineEndings(" "));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        lock (_gate)
        {
            foreach (var client in _clients)
            {
                client.Writer.TryComplete();
            }
        }

        _listener.Close();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or HttpListenerException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private bool Authorized(HttpListenerRequest request)
    {
        var presented = request.Headers["Authorization"] is { } header && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : request.QueryString["token"] ?? string.Empty;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(Token));
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            // A browser page on another origin must not be able to drive the device, so no CORS headers are ever sent.
            if (!Authorized(context.Request))
            {
                await WriteAsync(response, 401, "error missing or wrong token").ConfigureAwait(false);
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod;
            if (method == "GET" && path == "/ping")
            {
                await WriteAsync(response, 200, "ok").ConfigureAwait(false);
            }
            else if (method == "POST" && path == "/command")
            {
                using var body = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var command = (await body.ReadToEndAsync(_stop.Token).ConfigureAwait(false)).Trim();
                var result = await SessionCommands.ExecuteAsync(_session, _encodeText, command, _stop.Token).ConfigureAwait(false);
                await WriteAsync(response, result == "ok" ? 200 : 400, result).ConfigureAwait(false);
            }
            else if (method == "GET" && path == "/events")
            {
                await StreamEventsAsync(response).ConfigureAwait(false);
            }
            else
            {
                await WriteAsync(response, 404, "error no such endpoint (GET /ping, POST /command, GET /events)").ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is HttpListenerException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went away, or the server is shutting down.
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    private async Task StreamEventsAsync(HttpListenerResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;
        var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_gate)
        {
            _clients.Add(queue);
        }

        try
        {
            await using var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false)) { AutoFlush = true };
            await writer.WriteAsync(": connected\n\n").ConfigureAwait(false);
            await foreach (var line in queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                await writer.WriteAsync("data: " + line + "\n\n").ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _clients.Remove(queue);
            }
        }
    }

    private static async Task WriteAsync(HttpListenerResponse response, int status, string text)
    {
        response.StatusCode = status;
        response.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
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
