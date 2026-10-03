using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;

namespace DevTerm.Core.Sessions;

/// <summary>
/// Tails another dev-term process's session over the named pipe a <see cref="SessionPipeServer"/> publishes (the
/// <c>--attach &lt;name&gt;</c> mode). Read-only. See docs/design/proposals/cross-process-control-channel.md.
/// </summary>
public static class SessionPipeClient
{
    /// <summary>Connects to the named session and yields its lines (<c>open</c>, <c>rx HEX</c>, <c>tx HEX</c>, <c>closed ...</c>) until the server ends or <paramref name="cancellationToken"/> is cancelled.</summary>
    /// <exception cref="TimeoutException">No session of that name is publishing within <paramref name="connectTimeoutMs"/>.</exception>
    public static async IAsyncEnumerable<string> ReadLinesAsync(string sessionName, int connectTimeoutMs = 5000, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        await using var pipe = new NamedPipeClientStream(".", SessionPipeServer.PipeNameFor(sessionName), PipeDirection.In);
        await pipe.ConnectAsync(connectTimeoutMs, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            yield return line;
        }
    }

    /// <summary>Adds the printable ASCII of an <c>rx</c>/<c>tx</c> line's bytes after it; any other line comes back unchanged.</summary>
    public static string Describe(string line)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0 || (!line.StartsWith("rx ", StringComparison.Ordinal) && !line.StartsWith("tx ", StringComparison.Ordinal)))
        {
            return line;
        }

        try
        {
            var text = new StringBuilder();
            foreach (var b in Convert.FromHexString(line[(space + 1)..]))
            {
                text.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            return $"{line}  |{text}|";
        }
        catch (FormatException)
        {
            return line;
        }
    }
}
