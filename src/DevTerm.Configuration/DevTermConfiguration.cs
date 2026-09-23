using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DevTerm.Configuration;

/// <summary>
/// Composes a front end's configuration sources explicitly (rather than relying on
/// <see cref="Host.CreateDefaultBuilder(string[])"/>'s built-in chain) so a personal, untracked
/// settings file sits at exactly the precedence a "saved profile" needs: below environment
/// variables and command-line overrides, but above the shipped defaults. Shared by every front
/// end so the same profile works whichever one you run.
/// </summary>
public static class DevTermConfiguration
{
    /// <summary>Environment variables are only read with this prefix (stripped when bound), e.g. <c>DEVTERM_PORT=COM3</c>.</summary>
    public const string EnvironmentVariablePrefix = "DEVTERM_";

    /// <summary>An optional, untracked JSON file for a machine-local default profile (e.g. "COM3 4800 8N1 ascii"). Not meant to be committed — see .gitignore.</summary>
    public const string LocalSettingsFileName = "appsettings.Local.json";

    /// <summary>
    /// Precedence, lowest to highest: appsettings.json &lt; appsettings.&lt;environment&gt;.json
    /// &lt; appsettings.Local.json (a personal saved profile) &lt; environment variables (DEVTERM_*)
    /// &lt; command-line arguments.
    /// </summary>
    public static void Configure(HostBuilderContext context, IConfigurationBuilder config, string[] args) =>
        Configure(config, args, context.HostingEnvironment.EnvironmentName);

    /// <summary>
    /// Same layering as the <see cref="HostBuilderContext"/> overload, usable before a
    /// <see cref="IHost"/> exists — e.g. to bind and validate <see cref="CliOptions"/> early enough
    /// to decide whether to show a Configure screen instead of building the DI graph at all. See
    /// docs/design/connection-profiles.md's startup flow.
    /// </summary>
    public static void Configure(IConfigurationBuilder config, string[] args, string environmentName)
    {
        config.Sources.Clear();
        config.SetBasePath(AppContext.BaseDirectory);
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        config.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false);
        config.AddJsonFile(LocalSettingsFileName, optional: true, reloadOnChange: false);
        config.AddEnvironmentVariables(EnvironmentVariablePrefix);
        config.AddCommandLine(args);
    }

    /// <summary>
    /// Binds <paramref name="configuration"/> onto <paramref name="options"/> — plain
    /// <c>configuration.Bind(options)</c> plus the one case it can't handle:
    /// <see cref="CliOptions.Presenter"/> is a list, but a command-line flag (<c>--presenter ascii,hex</c>),
    /// an environment variable, or a profile saved before it became a list (<c>"Presenter": "hex"</c>)
    /// supplies it as a single scalar, which the binder would silently drop for an array property.
    /// A scalar is split on commas; being a scalar means it came from an environment variable/command
    /// line (which outrank any JSON layer) or from an old-format profile, so it wins over any array
    /// bound alongside it. Every place that binds a <see cref="CliOptions"/> goes through this.
    /// </summary>
    public static void Bind(IConfiguration configuration, CliOptions options)
    {
        configuration.Bind(options);

        if (configuration.GetSection(nameof(CliOptions.Presenter)).Value is { Length: > 0 } scalar)
        {
            options.Presenter = scalar.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>Overwrites the untracked default profile (<see cref="LocalSettingsFileName"/>) with the connection-relevant subset of <paramref name="options"/> — see <see cref="ToProfileJson"/>.</summary>
    public static void SaveLocalProfile(CliOptions options) =>
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, LocalSettingsFileName), ToProfileJson(options));

    /// <summary>
    /// Projects the connection-relevant subset of <paramref name="options"/> — transport settings,
    /// presenter, line ending, and an optional device-manifest name — as indented JSON, in the
    /// same shape whether saved as the untracked default profile or a named one (see
    /// docs/design/connection-profiles.md; both are bound back via the same
    /// <c>Microsoft.Extensions.Configuration.Json</c> + <c>Bind()</c> pipeline, not a separate
    /// parallel type). Deliberately excludes one-shot action/mode flags
    /// (<see cref="CliOptions.ListPorts"/>, <see cref="CliOptions.Tui"/>, <see cref="CliOptions.Cli"/>,
    /// <see cref="CliOptions.ListHidDevices"/>) — those describe how this particular run was
    /// invoked, not the device connection itself.
    /// </summary>
    public static string ToProfileJson(CliOptions options)
    {
        var profile = new Dictionary<string, object?>
        {
            [nameof(CliOptions.Transport)] = options.Transport,
            [nameof(CliOptions.Presenter)] = options.EffectivePresenters,
            [nameof(CliOptions.Parser)] = options.EffectiveParser,
            [nameof(CliOptions.LineEnding)] = options.LineEnding.ToString(),
            [nameof(CliOptions.AsciiMaxLineLength)] = options.AsciiMaxLineLength,
        };

        if (string.Equals(options.Transport, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            profile[nameof(CliOptions.Host)] = options.Host;
            profile[nameof(CliOptions.TcpPort)] = options.TcpPort;
            profile[nameof(CliOptions.Listen)] = options.Listen;
        }
        else if (string.Equals(options.Transport, "hid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Transport, "usbtmc", StringComparison.OrdinalIgnoreCase))
        {
            profile[nameof(CliOptions.VendorId)] = options.VendorId;
            profile[nameof(CliOptions.ProductId)] = options.ProductId;
            if (options.SerialNumber is not null)
            {
                profile[nameof(CliOptions.SerialNumber)] = options.SerialNumber;
            }
        }
        else
        {
            profile[nameof(CliOptions.Port)] = options.Port;
            profile[nameof(CliOptions.Baud)] = options.Baud;
            profile[nameof(CliOptions.DataBits)] = options.DataBits;
            profile[nameof(CliOptions.Parity)] = options.Parity.ToString();
            profile[nameof(CliOptions.StopBits)] = options.StopBits.ToString();
            profile[nameof(CliOptions.Handshake)] = options.Handshake.ToString();
            profile[nameof(CliOptions.Dtr)] = options.Dtr;
            profile[nameof(CliOptions.Rts)] = options.Rts;
            profile[nameof(CliOptions.WriteTimeoutMs)] = options.WriteTimeoutMs;
            profile[nameof(CliOptions.ReadTimeoutMs)] = options.ReadTimeoutMs;
        }

        if (options.ManifestName is not null)
        {
            profile[nameof(CliOptions.ManifestName)] = options.ManifestName;
        }

        if (options.Description is not null)
        {
            profile[nameof(CliOptions.Description)] = options.Description;
        }

        return JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
    }
}
