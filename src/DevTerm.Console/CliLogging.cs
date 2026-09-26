using DevTerm.Configuration;
using DevTerm.Core.Sessions;
using DevTerm.Logging;

namespace DevTerm.Console;

/// <summary>
/// <c>--log</c> for the plain CLI loop: starts logger mode on the session before it connects, so
/// the log captures the connect itself. The TUI handles <c>--log</c> itself (File &gt; Stop Logging
/// needs to own the logger). See docs/design/session-logging.md.
/// </summary>
internal static class CliLogging
{
    /// <returns>The running logger (dispose it to stop), or <see langword="null"/> when <c>--log</c> isn't set or the file couldn't be created — reported on <paramref name="stderr"/>, since a failed log shouldn't stop the connection.</returns>
    public static SessionLogger? Start(Session session, CliOptions cliOptions, TextWriter stderr, ConnectionProfileStore? profileStore = null)
    {
        if (string.IsNullOrWhiteSpace(cliOptions.Log))
        {
            return null;
        }

        var profileName = (profileStore ?? new ConnectionProfileStore()).FindName(cliOptions);
        var path = SessionLogging.ResolveLogPath(cliOptions.Log, cliOptions, profileName, DateTimeOffset.Now);
        try
        {
            var logger = SessionLogging.Start(path, session, cliOptions, cliOptions.EffectiveParser, profileName, "cli");
            stderr.WriteLine($"Logging to {path}.");
            return logger;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"Could not start logging to '{path}': {ex.Message}");
            return null;
        }
    }
}
