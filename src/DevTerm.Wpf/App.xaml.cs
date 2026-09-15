using System.Windows;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DevTerm.Wpf;

/// <summary>
/// Composes the same DI graph the console app does (see <see cref="ServiceCollectionExtensions.AddDevTermFrontEnd"/>)
/// from the same layered configuration (command-line args, environment variables, or a saved
/// appsettings.Local.json profile — see <see cref="DevTermConfiguration"/>), then hands the
/// resolved session to <see cref="MainWindow"/>. See docs/design/frontends.md.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;
        var cliOptions = new CliOptions();

        var hostBuilder = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) => DevTermConfiguration.Configure(context, config, args))
            .ConfigureServices((context, services) =>
            {
                context.Configuration.Bind(cliOptions);

                services.AddOptions<CliOptions>().Bind(context.Configuration).ValidateOnStart();
                services.AddSingleton<IValidateOptions<CliOptions>, CliOptionsValidator>();

                var validation = new CliOptionsValidator().Validate(null, cliOptions);
                if (validation.Failed)
                {
                    throw new OptionsValidationException(nameof(CliOptions), typeof(CliOptions), validation.Failures);
                }

                services.AddDevTermFrontEnd(cliOptions);
            });

        IHost host;
        try
        {
            host = hostBuilder.Build();
        }
        catch (OptionsValidationException ex)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, ex.Failures),
                "dev-term — invalid configuration",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _host = host;

        var catalog = host.Services.GetRequiredService<PresenterCatalog>();
        if (!catalog.TryGet(cliOptions.Presenter, out var presenter))
        {
            MessageBox.Show(
                $"Unknown presenter '{cliOptions.Presenter}'. Available: {string.Join(", ", catalog.Names)}",
                "dev-term",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var transport = host.Services.GetRequiredService<ITransport>();
        var sessionFactory = host.Services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.Create(transport, new Pipeline([presenter]));

        var window = new MainWindow(session, presenter, cliOptions);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
