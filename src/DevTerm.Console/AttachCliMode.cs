using System.Text;
using DevTerm.Core.Sessions;

namespace DevTerm.Console;

/// <summary>
/// <c>--attach &lt;name&gt; --cli true</c>: the ordinary REPL against another dev-term process's session (started with
/// <c>--control &lt;name&gt;</c>). A typed line is sent through that session's own parser and line ending; traffic and
/// replies print as they arrive. See docs/design/architecture.md (cross-session scripting).
/// </summary>
/// <remarks>
/// Lines starting with <c>:</c> are commands for the attach itself: <c>:hex AA BB</c> sends raw bytes, <c>:ping</c> checks
/// the session is there, <c>:quit</c> leaves. A line that must begin with a colon is sent as <c>::text</c>.
/// </remarks>
public static class AttachCliMode
{
    /// <summary>Maps one typed line to a control-pipe command, or null to stop (<c>:quit</c>) or skip an empty line.</summary>
    internal static (string? Command, bool Quit) ToCommand(string line)
    {
        if (line.Length == 0)
        {
            return (null, false);
        }

        if (!line.StartsWith(':'))
        {
            return ("send " + line, false);
        }

        if (line.StartsWith("::", StringComparison.Ordinal))
        {
            return ("send " + line[1..], false);
        }

        var parts = line[1..].Split(' ', 2, StringSplitOptions.TrimEntries);
        return parts[0].ToLowerInvariant() switch
        {
            "hex" when parts.Length == 2 => ("sendhex " + parts[1], false),
            "ping" => ("ping", false),
            "quit" or "exit" => (null, true),
            _ => ("send " + line, false),
        };
    }

    /// <summary>The attach loop over explicit streams so tests can drive it. Returns the process exit code.</summary>
    internal static async Task<int> RunAsync(string sessionName, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default)
    {
        async IAsyncEnumerable<string> Commands()
        {
            while (!cancellationToken.IsCancellationRequested && await stdin.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var (command, quit) = ToCommand(line);
                if (quit)
                {
                    yield break;
                }

                if (command is not null)
                {
                    yield return command;
                }
            }
        }

        try
        {
            await SessionControlClient.RunAsync(sessionName, Commands(), l => stdout.WriteLine(SessionPipeClient.Describe(l)), cancellationToken: cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (TimeoutException)
        {
            stderr.WriteLine($"No session named '{sessionName}' is accepting control. Start one with --control {sessionName}.");
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }
}
