using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DevTerm.Console;

/// <summary>
/// Composes the CLI mode's configuration sources explicitly (rather than relying on
/// <see cref="Host.CreateDefaultBuilder(string[])"/>'s built-in chain) so a personal, untracked
/// settings file sits at exactly the precedence a "saved profile" needs: below environment
/// variables and command-line overrides, but above the shipped defaults.
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
    public static void Configure(HostBuilderContext context, IConfigurationBuilder config, string[] args)
    {
        config.Sources.Clear();
        config.SetBasePath(AppContext.BaseDirectory);
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false);
        config.AddJsonFile(LocalSettingsFileName, optional: true, reloadOnChange: false);
        config.AddEnvironmentVariables(EnvironmentVariablePrefix);
        config.AddCommandLine(args);
    }
}
