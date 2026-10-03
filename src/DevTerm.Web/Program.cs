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

var validation = new CliOptionsValidator().Validate(null, cliOptions);
if (validation.Failed)
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
    await built.App.RunAsync();
}

return 0;
