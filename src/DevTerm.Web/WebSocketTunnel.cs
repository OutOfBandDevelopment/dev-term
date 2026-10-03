using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace DevTerm.Web;

/// <summary>
/// <c>/ws</c>: a text WebSocket onto the shared session. Each text frame received is one typed line to send; each output or
/// status line is sent back as one text frame (the backlog first). Scriptable, and what the browser page's behavior mirrors.
/// </summary>
internal static class WebSocketTunnel
{
    public static async Task HandleAsync(HttpContext context, SessionHub hub)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var outgoing = Channel.CreateUnbounded<string>();
        void OnLine(string line) => outgoing.Writer.TryWrite(line);

        hub.LineReceived += OnLine;
        foreach (var line in hub.Backlog)
        {
            outgoing.Writer.TryWrite(line);
        }

        var abort = context.RequestAborted;
        var writer = Task.Run(
            async () =>
            {
                await foreach (var line in outgoing.Reader.ReadAllAsync(abort))
                {
                    await socket.SendAsync(Encoding.UTF8.GetBytes(line), WebSocketMessageType.Text, true, abort);
                }
            },
            CancellationToken.None);

        try
        {
            var buffer = new byte[16 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                var length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(length), abort);
                    length += result.Count;
                }
                while (!result.EndOfMessage && length < buffer.Length);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var error = await hub.SendLineAsync(Encoding.UTF8.GetString(buffer, 0, length), abort);
                    if (error is not null)
                    {
                        outgoing.Writer.TryWrite($"! {error}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // The viewer went away.
        }
        finally
        {
            hub.LineReceived -= OnLine;
            outgoing.Writer.TryComplete();
            try
            {
                await writer;
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                // Same.
            }
        }
    }
}
