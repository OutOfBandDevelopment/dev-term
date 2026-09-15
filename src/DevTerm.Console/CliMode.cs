using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;

namespace DevTerm.Console;

/// <summary>
/// The scriptable/interactive console loop: connect, print `[presenter] text` per line of
/// output, read stdin for lines to send. See docs/design/frontends.md.
/// </summary>
public static class CliMode
{
    public static async Task<int> RunAsync(Session session, IPresenter presenter, CliOptions cliOptions)
    {
        session.Output += (_, output) => System.Console.WriteLine($"[{output.PresenterName}] {output.Text}");

        try
        {
            await session.OpenAsync();
        }
        catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
        {
            System.Console.Error.WriteLine(ConnectionErrorMessages.For(cliOptions.Transport, ex));
            return 1;
        }

        System.Console.WriteLine($"Connected to {ConnectionDescription.For(cliOptions)} using '{presenter.Name}'.");
        System.Console.WriteLine("Type a line and press Enter to send; Ctrl+C to exit.");

        using var cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        while (true)
        {
            string? line;
            try
            {
                // A plain Console.ReadLine() blocks on the OS read and ignores cts entirely, so
                // Ctrl+C would set the flag but never unblock the loop; ReadLineAsync(CancellationToken)
                // actually interrupts a pending interactive console read.
                line = await System.Console.In.ReadLineAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            if (presenter is IPresenterInput input)
            {
                // An empty typed line has no coherent "send" for any transport, and for HID it's
                // actively invalid (report writes must match a fixed, non-zero device-defined
                // length; a real device surfaced this as an unhandled Win32 error before this
                // guard existed) - matches the equivalent guard already in TuiMode.
                var payload = cliOptions.LineEnding.Append(input.Parse(line));
                if (payload.Length == 0)
                {
                    continue;
                }

                try
                {
                    await session.SendAsync(payload);
                }
                catch (TimeoutException)
                {
                    System.Console.Error.WriteLine(
                        "Send timed out — no response to hardware flow control (CTS)? Check the device or --handshake.");
                }
                catch (Exception ex) when (ConnectionErrorMessages.IsConnectionFailure(ex))
                {
                    // E.g. a HID write whose length doesn't match the device's exact report size
                    // - framing a valid report for a specific device is a device-specific
                    // decoder/control-surface concern, not something a generic text presenter's
                    // raw typed input can guarantee, so report it rather than crash.
                    System.Console.Error.WriteLine($"Send failed: {ex.Message}");
                }
            }
            else
            {
                System.Console.Error.WriteLine($"Presenter '{presenter.Name}' does not support sending.");
            }
        }

        await session.CloseAsync();
        return 0;
    }
}
