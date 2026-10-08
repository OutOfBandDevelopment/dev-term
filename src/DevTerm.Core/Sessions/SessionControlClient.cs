using System.IO.Pipes;
using System.Text;

namespace DevTerm.Core.Sessions;

/// <summary>
/// Talks to another dev-term process's <see cref="SessionControlPipeServer"/> (the <c>--controlclient &lt;name&gt;</c> mode):
/// commands out, reply and event lines back.
/// </summary>
public static class SessionControlClient
{
    /// <summary>
    /// Connects to the named session, writes each line of <paramref name="commands"/> as a command and passes every line the
    /// session sends to <paramref name="onLine"/>, until <paramref name="commands"/> ends (then waits briefly for the last reply) or cancellation.
    /// </summary>
    /// <exception cref="TimeoutException">No session of that name is accepting control within <paramref name="connectTimeoutMs"/>.</exception>
    public static async Task RunAsync(string sessionName, IAsyncEnumerable<string> commands, Action<string> onLine, int connectTimeoutMs = 5000, int drainMs = 500, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        await using var pipe = new NamedPipeClientStream(".", SessionControlPipeServer.PipeNameFor(sessionName), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(connectTimeoutMs, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n", AutoFlush = true };
        using var readerStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = Task.Run(async () =>
        {
            try
            {
                while (await reader.ReadLineAsync(readerStop.Token).ConfigureAwait(false) is { } line)
                {
                    onLine(line);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var command in commands.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // The session ended; its last lines were already passed to onLine.
        }

        await Task.WhenAny(pump, Task.Delay(drainMs, CancellationToken.None)).ConfigureAwait(false);
        await readerStop.CancelAsync().ConfigureAwait(false);
        await pump.ConfigureAwait(false);
        try
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }
}
