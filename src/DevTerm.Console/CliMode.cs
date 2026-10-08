using DevTerm.Devices.Scpi;
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
    internal static Task<int> RunAsync(Session session, PresenterCatalog catalog, CliOptions cliOptions, TextReader stdin, TextWriter stdout, TextWriter stderr) =>
        RunAsync(session, catalog, cliOptions, stdin, stdout, stderr, new CancellationTokenSource());

    /// <summary>
    /// Same as the six-argument overload, but with the Ctrl+C cancellation source itself
    /// injectable - production callers get a fresh one wired to the real
    /// <see cref="System.Console.CancelKeyPress"/>; a test can supply its own and call
    /// <see cref="CancellationTokenSource.Cancel()"/> directly to simulate Ctrl+C without needing
    /// the real (internally-constructed) console event.
    /// </summary>
    internal static async Task<int> RunAsync(Session session, PresenterCatalog catalog, CliOptions cliOptions, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationTokenSource shutdownRequested)
    {
        var parser = cliOptions.EffectiveParser;
        if (!catalog.TryGetInput(parser, out var input))
        {
            stderr.WriteLine($"Unknown parser '{parser}'. Available: {string.Join(", ", catalog.InputNames)}");
            return 1;
        }

        // The TUI/WPF control panels do this when they open; here a named profile does it up front, so a terminatorless
        // instrument (Rigol DS1102E, Korad) doesn't leave the scpi presenter buffering for a line ending that never comes.
        if (cliOptions.ScpiProfile is { Length: > 0 } scpiProfileName
            && ScpiProfileCatalog.All.FirstOrDefault(p => string.Equals(p.Name, scpiProfileName, StringComparison.OrdinalIgnoreCase)) is { } scpiProfile
            && catalog.TryGet("scpi", out var scpiPresenter)
            && scpiPresenter is ScpiReplyPresenter scpiReplyPresenter)
        {
            scpiReplyPresenter.ConfigureTerminator(scpiProfile.Terminator);
        }

        session.Output += (_, output) => stdout.WriteLine($"[{output.PresenterName}] {output.Text}");
        session.Disconnected += (_, e) =>
            stderr.WriteLine($"{ConnectionErrorMessages.ForDisconnect(cliOptions.Transport, e.Error)} The next line you send will reconnect.");

        await using var pipeServer = string.IsNullOrWhiteSpace(cliOptions.Pipe) ? null : new SessionPipeServer(cliOptions.Pipe);
        using var pipeRegistration = pipeServer is null ? null : session.AddObserver(pipeServer);
        if (pipeServer is not null)
        {
            stderr.WriteLine($"Publishing this session read-only; attach with: --attach {cliOptions.Pipe}");
        }

        await using var controlServer = string.IsNullOrWhiteSpace(cliOptions.Control)
            ? null
            : new SessionControlPipeServer(session, cliOptions.Control, text =>
                TypedInput.TryEncode(input, parser, text, cliOptions.LineEnding, out var bytes, out var encodeError) ? (bytes, null) : (null, encodeError));
        using var controlRegistration = controlServer is null ? null : session.AddObserver(controlServer);
        if (controlServer is not null)
        {
            stderr.WriteLine($"Accepting commands (send, sendhex, ping) for this session on the local pipe {controlServer.PipeName}, current user only.");
        }

        await using var shareServer = cliOptions.ShareTcp > 0 && System.Net.IPAddress.TryParse(cliOptions.ShareBind, out var shareAddress)
            ? new SessionTcpShareServer(session, shareAddress, cliOptions.ShareTcp)
            : null;
        using var shareRegistration = shareServer is null ? null : session.AddObserver(shareServer);
        if (shareServer is not null)
        {
            stderr.WriteLine($"Sharing this session on {cliOptions.ShareBind}:{shareServer.Port} (no authentication; one client at a time).");
        }

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

        using var cts = shutdownRequested;
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
                    // stdin.ReadLineAsync(cts.Token) does not reliably observe cts here: Console.In is
                    // a SyncTextReader, whose ReadLineAsync(CancellationToken) only checks the token
                    // once before starting a plain synchronous read - once that read is blocked waiting
                    // for a line, cts.Cancel() from Ctrl+C above has no way to unblock it (confirmed on
                    // real Linux: the CancelKeyPress handler fires immediately, but the pending read
                    // stays blocked until Enter is actually pressed). Racing it against a genuinely
                    // cancellable Task.Delay lets this loop react to Ctrl+C immediately regardless -
                    // the real read task, if still pending, is simply abandoned (a background
                    // thread-pool wait that doesn't block process exit), the same tradeoff already
                    // accepted for SerialPort/HidStream's own uncancellable blocking reads elsewhere in
                    // this codebase.
                    var readTask = stdin.ReadLineAsync(cts.Token).AsTask();
                    var cancelTask = Task.Delay(Timeout.Infinite, cts.Token);
                    if (await Task.WhenAny(readTask, cancelTask) == cancelTask)
                    {
                        break;
                    }

                    line = await readTask;
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
