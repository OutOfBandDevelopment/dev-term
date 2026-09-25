using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;

namespace DevTerm.Console;

/// <summary>
/// The scriptable/interactive console loop: connect, print `[presenter] text` per line of
/// output, read stdin for lines to send. See docs/design/frontends.md.
/// </summary>
/// <remarks>
/// Errors never end the loop. A line the parser can't encode is rejected (the connection is left
/// alone); a lost connection - a read or send failure, or the device hanging up - is reported
/// once (via <see cref="Session.Disconnected"/>), and the next line typed reconnects before it's
/// sent. Only a failure to make the initial connection exits (code 1), so a script still sees it.
/// </remarks>
public static class CliMode
{
    /// <remarks>
    /// Typed lines are encoded by the profile's parser (<see cref="CliOptions.EffectiveParser"/> —
    /// <c>--parser</c>) for the whole run; there's no per-line switch here, since a plain stdin loop
    /// has no non-colliding way to say "this line is hex" (the TUI/WPF have a control for it).
    /// </remarks>
    public static Task<int> RunAsync(Session session, PresenterCatalog catalog, CliOptions cliOptions) =>
        RunAsync(session, catalog, cliOptions, System.Console.In, System.Console.Out, System.Console.Error);

    /// <summary>The same loop over explicit readers/writers, so tests can drive it without a real console.</summary>
    internal static async Task<int> RunAsync(Session session, PresenterCatalog catalog, CliOptions cliOptions, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        var parser = cliOptions.EffectiveParser;
        if (!catalog.TryGetInput(parser, out var input))
        {
            stderr.WriteLine($"Unknown parser '{parser}'. Available: {string.Join(", ", catalog.InputNames)}");
            return 1;
        }

        session.Output += (_, output) => stdout.WriteLine($"[{output.PresenterName}] {output.Text}");
        session.Disconnected += (_, e) =>
            stderr.WriteLine($"{ConnectionErrorMessages.ForDisconnect(cliOptions.Transport, e.Error)} The next line you send will reconnect.");

        try
        {
            await session.OpenAsync();
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ConnectionErrorMessages.For(cliOptions.Transport, ex));
            return 1;
        }

        if (ManifestNameWarning.For(cliOptions) is { } manifestWarning)
        {
            stderr.WriteLine(manifestWarning);
        }

        stdout.WriteLine($"Connected to {ConnectionDescription.For(cliOptions)} using '{string.Join(", ", cliOptions.EffectivePresenters)}' (send as '{parser}').");
        stdout.WriteLine("Type a line and press Enter to send; Ctrl+C to exit.");

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        System.Console.CancelKeyPress += onCancel;

        try
        {
            while (true)
            {
                string? line;
                try
                {
                    // A plain Console.ReadLine() blocks on the OS read and ignores cts entirely, so
                    // Ctrl+C would set the flag but never unblock the loop; ReadLineAsync(CancellationToken)
                    // actually interrupts a pending interactive console read.
                    line = await stdin.ReadLineAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    stderr.WriteLine($"Stopped reading input: {ex.Message}");
                    break;
                }

                if (line is null)
                {
                    break;
                }

                await SendLineAsync(session, cliOptions, input, parser, line, stdout, stderr);
            }
        }
        finally
        {
            System.Console.CancelKeyPress -= onCancel;
        }

        await session.CloseAsync();
        return 0;
    }

    private static async Task SendLineAsync(Session session, CliOptions cliOptions, IPresenterInput input, string parser, string line, TextWriter stdout, TextWriter stderr)
    {
        // An empty typed line has no coherent "send" for any transport, and for HID it's actively
        // invalid (report writes must match a fixed, non-zero device-defined length; a real device
        // surfaced this as an unhandled Win32 error before this guard existed).
        if (line.Length == 0)
        {
            return;
        }

        if (!TypedInput.TryEncode(input, parser, line, cliOptions.LineEnding, out var payload, out var error))
        {
            stderr.WriteLine(error);
            return;
        }

        if (payload.Length == 0)
        {
            return;
        }

        if (session.State != ConnectionState.Open)
        {
            stderr.WriteLine($"Reconnecting to {ConnectionDescription.For(cliOptions)}...");
            try
            {
                await session.OpenAsync();
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"{ConnectionErrorMessages.For(cliOptions.Transport, ex)} Not sent.");
                return;
            }

            stdout.WriteLine($"Reconnected to {ConnectionDescription.For(cliOptions)}.");
        }

        try
        {
            await session.SendAsync(payload);
        }
        catch (Exception ex)
        {
            // A device-side failure has already disconnected the session and been reported through
            // Session.Disconnected; anything else (the connection dropped between the check above
            // and the send) is reported here.
            if (session.State == ConnectionState.Open)
            {
                stderr.WriteLine($"Send failed: {ex.Message}");
            }
        }
    }
}
