using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DevTerm.Configuration;
using DevTerm.Core.Plugins;
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

        // A safety net, not a substitute for the deliberate try/catch-and-report handling already in
        // MainWindow (ConnectAsync/ToggleConnectionAsync/SwitchProfileAsync) - those catch a known,
        // expected failure (ConnectionErrorMessages.IsConnectionFailure) and intentionally close on a
        // failed *startup* connect. This instead catches whatever slips past every existing catch
        // block (a genuine bug, an exception type nobody anticipated) on the UI thread, reports it,
        // and lets the app keep running rather than taking the whole window down with it.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // A faulted background Task whose exception nobody ever observed (no await, no .Result, no
        // continuation checking it) used to crash the process when its finalizer ran - .NET no longer
        // does that by default, but this still reports it instead of letting it vanish silently, and
        // marks it observed so nothing downstream re-escalates it.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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
        var layeredConfig = earlyConfigBuilder.Build();

        // A bad value on the command line, in an environment variable, or in a corrupt/truncated
        // saved profile (e.g. --baud fast) throws from inside the configuration binder before
        // validation ever runs - see docs/bugs/resolved/032-startup-bind-failure-crash.md. cliOptions is
        // reset to a clean default so it doesn't carry whatever partial state a failed Bind left
        // behind into the Device Profiles editor below.
        string? bindError = null;
        try
        {
            DevTermConfiguration.Bind(layeredConfig, cliOptions);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or InvalidDataException)
        {
            cliOptions = new CliOptions();
            bindError = ex.Message;
        }

        // The theme is an app preference, not part of the connection: --theme / DEVTERM_THEME for this
        // run, else the saved View > Theme choice, else "system" - applied before the first window
        // (even the startup Device Profiles editor below). Problems show in MainWindow's output.
        RetentionSweeper.Sweep(new AppPreferencesStore().Load());
        ActiveTheme.Initialize(layeredConfig);
        WpfTheme.AttachApplication(this);

        // "system" follows Windows' app mode live: re-resolve when the user flips it in Settings.
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color)
            {
                Dispatcher.BeginInvoke(ActiveTheme.RefreshSystem);
            }
        };

        if (bindError is null)
        {
            var validation = new CliOptionsValidator().Validate(null, cliOptions);
            if (validation.Failed)
            {
                bindError = string.Join(" ", validation.Failures);
            }
        }

        if (bindError is not null)
        {
            var editor = new DeviceProfilesWindow(new ConnectionProfileStore(), cliOptions, bindError);
            var accepted = editor.ShowDialog();
            if (accepted != true || editor.Result is null)
            {
                Shutdown(0);
                return;
            }

            cliOptions = editor.Result;
        }

        PluginTrust.Approver = new MessageBoxPluginApprover();

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

        // --pipe publishes the first tab's session read-only for `--attach` (the main window's other tabs are not published).
        if (!string.IsNullOrWhiteSpace(cliOptions.Pipe))
        {
            _pipeServer = new SessionPipeServer(cliOptions.Pipe);
            _pipeRegistration = session.AddObserver(_pipeServer);
        }

        if (!string.IsNullOrWhiteSpace(cliOptions.Control))
        {
            _controlServer = new SessionControlPipeServer(session, cliOptions.Control, text => TypedInput.TryEncode(catalog, cliOptions, text));
            _controlRegistration = session.AddObserver(_controlServer);
        }

        if (cliOptions.ControlHttp > 0)
        {
            _controlHttpServer = new SessionHttpControlServer(session, cliOptions.ControlHttp, text => TypedInput.TryEncode(catalog, cliOptions, text), cliOptions.ControlToken);
            _controlHttpRegistration = session.AddObserver(_controlHttpServer);
        }

        if (cliOptions.ShareTcp > 0 && System.Net.IPAddress.TryParse(cliOptions.ShareBind, out var shareAddress))
        {
            if (cliOptions.ShareRfc2217)
            {
                _rfc2217Server = new DevTerm.Transports.Rfc2217.Rfc2217ServerBridge(session, shareAddress, cliOptions.ShareTcp);
                _shareRegistration = session.AddObserver(_rfc2217Server);
            }
            else
            {
                _shareServer = new SessionTcpShareServer(session, shareAddress, cliOptions.ShareTcp);
                _shareRegistration = session.AddObserver(_shareServer);
            }
        }

        var window = new MainWindow(session, catalog, cliOptions) { Plugins = host.Services.GetService<IReadOnlyList<PluginLoadResult>>(), PluginPanels = [.. host.Services.GetServices<DevTerm.Core.Control.IDevicePanelContribution>()] };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    private SessionTcpShareServer? _shareServer;
    private DevTerm.Transports.Rfc2217.Rfc2217ServerBridge? _rfc2217Server;
    private IDisposable? _shareRegistration;
    private SessionHttpControlServer? _controlHttpServer;
    private IDisposable? _controlHttpRegistration;
    private SessionControlPipeServer? _controlServer;
    private IDisposable? _controlRegistration;
    private SessionPipeServer? _pipeServer;
    private IDisposable? _pipeRegistration;

    protected override void OnExit(ExitEventArgs e)
    {
        _shareRegistration?.Dispose();
        _shareServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _rfc2217Server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _controlHttpRegistration?.Dispose();
        _controlHttpServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _controlRegistration?.Dispose();
        _controlServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _pipeRegistration?.Dispose();
        _pipeServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host?.Dispose();
        base.OnExit(e);
    }

    // Setting e.Handled = true is what tells WPF the process shouldn't terminate over this - left
    // unset (the default), the runtime tears the app down right after this handler returns regardless
    // of what it does. Shown via a plain MessageBox (not ConnectionErrorMessages, which is specific to
    // transport open/send failures) since this covers arbitrary, unanticipated exceptions.
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred and has been ignored so dev-term can keep running:\n\n{e.Exception}",
            "dev-term — unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();

        // Raised on the finalizer thread, not the UI thread - MessageBox.Show needs a real STA
        // message loop, so this marshals over rather than calling it directly here. Fire-and-forget:
        // there's no result to wait for, and the app (or Current itself) may already be shutting down.
        Current?.Dispatcher.BeginInvoke(() =>
            MessageBox.Show(
                $"A background operation failed and has been ignored so dev-term can keep running:\n\n{e.Exception}",
                "dev-term — unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error));
    }
}
