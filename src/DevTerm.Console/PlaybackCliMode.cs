using DevTerm.Configuration;
using DevTerm.Logging;
using DevTerm.Logging.Playback;

namespace DevTerm.Console;

/// <summary>
/// <c>--playback &lt;log&gt;</c>: replays a session log through the presenters and prints the
/// decoded output (one <see cref="PlaybackText"/> line per item), then exits — non-interactive, no
/// connection is made (only presenters are composed; see <see cref="PlaybackPresenters"/>).
/// <c>--presenter</c> overrides the log's own presenters; <c>--playbackspeed</c> paces it (0, the
/// default, is as fast as possible). See docs/user-guide/logging-and-playback.md.
/// </summary>
public static class PlaybackCliMode
{
    public static async Task<int> RunAsync(CliOptions cliOptions, IReadOnlyList<string> presenterOverride)
    {
        // Ctrl+C stops a paced (--playbackspeed) playback cleanly instead of killing the process mid-line.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        System.Console.CancelKeyPress += onCancel;
        try
        {
            return await RunAsync(cliOptions, presenterOverride, System.Console.Out, System.Console.Error, TimeProvider.System, cts.Token);
        }
        finally
        {
            System.Console.CancelKeyPress -= onCancel;
        }
    }

    /// <param name="presenterOverride">Presenters named on the command line, or empty to use the log's own.</param>
    internal static async Task<int> RunAsync(CliOptions cliOptions, IReadOnlyList<string> presenterOverride, TextWriter stdout, TextWriter stderr, TimeProvider clock, CancellationToken cancellationToken)
    {
        var path = cliOptions.Playback!;
        var presenters = new PlaybackPresenters(cliOptions);

        PlaybackController controller;
        try
        {
            controller = presenters.Open(path, clock);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SessionLogFormatException)
        {
            stderr.WriteLine($"Could not open '{path}' for playback: {ex.Message}");
            return 1;
        }

        if (presenterOverride.Count > 0)
        {
            var unknown = presenterOverride.Where(p => !presenters.Names.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (unknown.Length > 0)
            {
                stderr.WriteLine($"Unknown presenter '{unknown[0]}'. Available: {string.Join(", ", presenters.Names)}");
                return 1;
            }

            controller.SetPresenters(presenterOverride);
        }

        foreach (var warning in controller.Log.Warnings)
        {
            stderr.WriteLine(warning);
        }

        stderr.WriteLine($"Playing {Path.GetFileName(path)} ({controller.Description}) through '{string.Join(", ", controller.Presenters)}'.");

        if (cliOptions.PlaybackSpeed < 0 || double.IsNaN(cliOptions.PlaybackSpeed))
        {
            stderr.WriteLine($"--playbackspeed must be 0 (as fast as possible) or a positive rate, got {cliOptions.PlaybackSpeed}.");
            return 1;
        }

        controller.Engine.Speed = cliOptions.PlaybackSpeed == 0 ? double.PositiveInfinity : cliOptions.PlaybackSpeed;
        controller.Engine.Play();

        try
        {
            while (!controller.Engine.IsAtEnd)
            {
                foreach (var line in PlaybackText.Lines(controller.Tick()))
                {
                    stdout.WriteLine(line.Text);
                }

                if (controller.Engine.TimeUntilNextDue() is { } wait && wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, clock, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        return 0;
    }
}
