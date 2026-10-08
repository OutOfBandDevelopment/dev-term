using DevTerm.Configuration;
using DevTerm.Web;
using Microsoft.Extensions.Configuration;

// Same layered configuration as the console app (appsettings.Local.json profile, DEVTERM_ env, command line),
// so one saved profile works here too. Web settings live under the "Web" section: --Web:Token x, --Web:Urls ...
var configBuilder = new ConfigurationBuilder();
DevTermConfiguration.Configure(configBuilder, args, "Production");
var configuration = configBuilder.Build();

var cliOptions = new CliOptions();
var webOptions = new WebOptions();
try
{
    DevTermConfiguration.Bind(configuration, cliOptions);
    configuration.GetSection(WebOptions.SectionName).Bind(webOptions);
}
catch (Exception ex) when (ex is InvalidOperationException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

if (!string.IsNullOrWhiteSpace(webOptions.Profile))
{
    var store = new ConnectionProfileStore();
    var name = store.List().FirstOrDefault(n => string.Equals(n, webOptions.Profile, StringComparison.OrdinalIgnoreCase));
    if (name is null)
    {
        Console.Error.WriteLine($"No saved profile named '{webOptions.Profile}'. Saved: {string.Join(", ", store.List())}.");
        return 1;
    }

    var project = cliOptions.Project;
    cliOptions = store.Load(name);
    cliOptions.Project = project;
}

// No connection configured at all is a valid start: the host comes up with its main session closed and connections are
// opened at runtime (POST /api/connections from a --project file). A half-configured connection is still an error.
var unconfigured = string.Equals(cliOptions.Transport, new CliOptions().Transport, StringComparison.OrdinalIgnoreCase)
    && string.IsNullOrEmpty(cliOptions.Port) && string.IsNullOrEmpty(cliOptions.Host);
var validation = new CliOptionsValidator().Validate(null, cliOptions);
if (validation.Failed && !unconfigured)
{
    Console.Error.WriteLine(string.Join(" ", validation.Failures));
    return 1;
}

WebHost.Built built;
try
{
    built = WebHost.Build(cliOptions, webOptions, args);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

await using (built.Hub)
{
    await built.Hub.StartAsync();
    var first = webOptions.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
    Console.WriteLine($"dev-term web: {first}/?token={built.Token}");
    if (built.ControlHttp is { } controlHttp)
    {
        Console.WriteLine($"dev-term control: http://127.0.0.1:{controlHttp.Port}/ with header 'Authorization: Bearer {controlHttp.Token}' (POST /command, GET /events, GET /ping).");
    }

    await built.App.RunAsync();
}

return 0;
