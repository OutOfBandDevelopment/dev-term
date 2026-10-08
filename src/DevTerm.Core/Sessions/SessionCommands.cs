namespace DevTerm.Core.Sessions;

/// <summary>The command lines a control channel accepts (<c>send</c>, <c>sendhex</c>, <c>ping</c>), shared by the pipe and HTTP servers.</summary>
internal static class SessionCommands
{
    /// <summary>Runs one command line against <paramref name="session"/>; returns <c>ok</c> or <c>error &lt;why&gt;</c>. Never throws except for cancellation.</summary>
    public static async Task<string> ExecuteAsync(Session session, Func<string, (byte[]? Payload, string? Error)> encodeText, string command, CancellationToken cancellationToken)
    {
        var space = command.IndexOf(' ', StringComparison.Ordinal);
        var verb = (space < 0 ? command : command[..space]).ToLowerInvariant();
        var argument = space < 0 ? string.Empty : command[(space + 1)..];
        try
        {
            switch (verb)
            {
                case "ping":
                    return "ok";
                case "send":
                {
                    var (payload, error) = encodeText(argument);
                    if (payload is null)
                    {
                        return "error " + (error ?? "could not encode that text").ReplaceLineEndings(" ");
                    }

                    await session.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                    return "ok";
                }

                case "sendhex":
                    await session.SendAsync(Convert.FromHexString(argument.Replace(" ", string.Empty, StringComparison.Ordinal)), cancellationToken).ConfigureAwait(false);
                    return "ok";
                default:
                    return "error unknown command '" + verb + "' (send, sendhex, ping)";
            }
        }
        catch (FormatException)
        {
            return "error sendhex needs an even number of hex digits";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return "error " + e.Message.ReplaceLineEndings(" ");
        }
    }
}
