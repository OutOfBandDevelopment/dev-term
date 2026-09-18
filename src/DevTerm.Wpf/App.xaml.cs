using System.Windows;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        // Don't quit when the startup DeviceProfilesWindow below closes (WPF's default
        // ShutdownMode is OnLastWindowClose) - there's no MainWindow yet at that point, so the
        // app would exit before ever getting to open one. Restored once MainWindow is actually set.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var args = e.Args;

        // Bind CliOptions from the same layered sources the host will use, but before building the
        // host at all — building it eagerly wires a transport from cliOptions (AddDevTermFrontEnd),
        // so an invalid CliOptions has to be caught here, ahead of that, to show the Device
        // Profiles editor instead of hard-failing — see docs/design/connection-profiles.md's
        // startup flow (mirrors DevTerm.Console's Program.cs).
        var earlyConfigBuilder = new ConfigurationBuilder();
        DevTermConfiguration.Configure(earlyConfigBuilder, args, Environments.Production);
        var cliOptions = new CliOptions();
        DevTermConfiguration.Bind(earlyConfigBuilder.Build(), cliOptions);

        var validation = new CliOptionsValidator().Validate(null, cliOptions);
        if (validation.Failed)
        {
            var editor = new DeviceProfilesWindow(new ConnectionProfileStore(), cliOptions, string.Join(" ", validation.Failures));
            var accepted = editor.ShowDialog();
            if (accepted != true || editor.Result is null)
            {
                Shutdown(0);
                return;
            }

            cliOptions = editor.Result;
        }

        var hostBuilder = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) => DevTermConfiguration.Configure(context, config, args))
            .ConfigureServices((_, services) => services.AddDevTermFrontEnd(cliOptions));

        var host = hostBuilder.Build();
        _host = host;

        var catalog = host.Services.GetRequiredService<PresenterCatalog>();
        IReadOnlyList<IPresenter> presenters;
        try
        {
            presenters = DevTermSessionBuilder.ResolvePresenters(catalog, cliOptions);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "dev-term", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var transport = host.Services.GetRequiredService<ITransport>();
        var sessionFactory = host.Services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.Create(transport, new Pipeline(presenters));

        var window = new MainWindow(session, catalog, cliOptions);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
