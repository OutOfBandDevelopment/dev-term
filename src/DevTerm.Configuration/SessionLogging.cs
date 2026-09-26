using System.Globalization;
using System.Reflection;
using DevTerm.Core.Sessions;
using DevTerm.Logging;

namespace DevTerm.Configuration;

/// <summary>
/// Front-end glue for logger mode: where a log goes by default, what its header says about the
/// connection (from a <see cref="CliOptions"/>), and starting/re-attaching a <see cref="SessionLogger"/>
/// the same way from the CLI, TUI and WPF. See docs/design/session-logging.md.
/// </summary>
public static class SessionLogging
{
    /// <summary>
    /// The <c>--log</c> value that means "pick a name for me" rather than a path — <c>--log true</c>,
    /// since the configuration binder has no bare-flag form (see CLAUDE.md).
    /// </summary>
    public const string AutoPathValue = "true";

    /// <summary>
    /// <paramref name="logOption"/> as a full path: <see cref="AutoPathValue"/> (or blank) becomes
    /// <see cref="DefaultLogPath"/>; anything else is taken as a path, relative to the working
    /// directory.
    /// </summary>
    public static string ResolveLogPath(string? logOption, CliOptions cliOptions, string? profileName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(logOption) || string.Equals(logOption.Trim(), AutoPathValue, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultLogPath(cliOptions, profileName, now);
        }

        return Path.GetFullPath(logOption.Trim());
    }

    /// <summary><c>~/.dev-term/logs/{yyyyMMdd-HHmmss}_{profile or connection}.jsonl</c>, with characters a file name can't hold replaced by <c>_</c>.</summary>
    public static string DefaultLogPath(CliOptions cliOptions, string? profileName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        var subject = profileName is { Length: > 0 } ? profileName : ConnectionDescription.Definition(cliOptions);
        var invalid = Path.GetInvalidFileNameChars().Concat([':', '/', '\\', '*', '?', ' ']).ToHashSet();
        var safe = new string([.. subject.Select(c => invalid.Contains(c) ? '_' : c)]).Trim('_');
        while (safe.Contains("__", StringComparison.Ordinal))
        {
            safe = safe.Replace("__", "_", StringComparison.Ordinal);
        }

        var stamp = now.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(DevTermUserDataPaths.LogsDirectory, $"{stamp}_{(safe.Length > 0 ? safe : "session")}{SessionLogFormat.FileExtension}");
    }

    /// <summary>The header for a log of <paramref name="cliOptions"/>'s connection.</summary>
    public static SessionLogHeader HeaderFor(CliOptions cliOptions, string parser, string? profileName, string frontEnd, DateTimeOffset created)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        var version = typeof(SessionLogging).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "unknown";
        return new SessionLogHeader
        {
            Created = created,
            Application = $"dev-term {version} ({frontEnd})",
            Connection = ConnectionDescription.Definition(cliOptions),
            Profile = profileName,
            Transport = cliOptions.Transport,
            Presenters = cliOptions.EffectivePresenters,
            Parser = parser,
        };
    }

    /// <summary>
    /// Starts logging <paramref name="session"/> to <paramref name="path"/> (replacing any file
    /// there) and attaches to it, so its current state is recorded straight away.
    /// </summary>
    public static SessionLogger Start(string path, Session session, CliOptions cliOptions, string parser, string? profileName, string frontEnd, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        clock ??= TimeProvider.System;
        var logger = SessionLogger.Start(path, HeaderFor(cliOptions, parser, profileName, frontEnd, clock.GetUtcNow()), clock);
        try
        {
            logger.Attach(session, ConnectionDescription.Definition(cliOptions), profileName);
        }
        catch
        {
            logger.Dispose();
            throw;
        }

        return logger;
    }

    /// <summary>Follows a live profile switch: the same log continues with <paramref name="session"/>, marked by a <c>session</c> record.</summary>
    public static void Follow(SessionLogger? logger, Session session, CliOptions cliOptions, string? profileName)
    {
        if (logger is { IsActive: true })
        {
            logger.Attach(session, ConnectionDescription.Definition(cliOptions), profileName);
        }
    }
}
