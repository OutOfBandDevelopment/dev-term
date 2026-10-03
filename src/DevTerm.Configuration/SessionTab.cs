using System.Buffers;
using DevTerm.Core.Presenters;
using DevTerm.Core.Routing;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;

namespace DevTerm.Configuration;

/// <summary>
/// Bundles one open connection's per-session state — everything <c>TuiMode.BuildWindow</c> and
/// <c>MainWindow</c> today hold as window-level fields/closure variables that get reassigned together
/// on a live profile switch (<see cref="Session"/>, <see cref="Catalog"/>, <see cref="CliOptions"/>,
/// the current send-format <see cref="Parser"/>), plus its own <see cref="SendHistory"/>. First step
/// toward docs/design/multi-session-ui.md's <c>SessionTab</c> — both front ends still hold exactly one
/// of these today; this is a pure extraction, not yet wired to a multi-tab UI.
/// </summary>
public sealed class SessionTab
{
    private Session _session;
    private readonly RoutingObserver _observer;
    private IDisposable _observerHandle;
    private RoutingService? _routing;

    /// <param name="services">
    /// The throwaway provider <see cref="DevTermSessionBuilder.Build"/> composed <paramref name="session"/>'s
    /// transport from, when this tab was built that way (a live profile switch, or a future "File &gt;
    /// New Session"). Null for the app's original startup connection, which is composed from the app's
    /// own long-lived host instead. Never disposed by this type — see
    /// <see cref="DevTermSessionBuilder.Result"/>'s own doc comment on why that's safe.
    /// </param>
    public SessionTab(Session session, PresenterCatalog catalog, CliOptions cliOptions, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(cliOptions);

        _session = session;
        _observer = new RoutingObserver(this);
        _observerHandle = session.AddObserver(_observer);
        Catalog = catalog;
        CliOptions = cliOptions;
        Services = services;
        Parser = cliOptions.EffectiveParser;
    }

    /// <summary>Reassigned, not replaced-by-a-new-tab, on a live profile switch — mirrors today's <c>TuiMode</c>/<c>MainWindow</c> field reassignment exactly.</summary>
    public Session Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
            {
                return;
            }

            _observerHandle.Dispose();
            _session = value;
            _observerHandle = value.AddObserver(_observer);
        }
    }

    public PresenterCatalog Catalog { get; set; }

    public CliOptions CliOptions { get; set; }

    public IServiceProvider? Services { get; set; }

    /// <summary>The send format currently encoding typed lines — starts as <see cref="CliOptions"/>'s <see cref="Configuration.CliOptions.EffectiveParser"/>, then follows "Send as" selections for this tab only.</summary>
    public string Parser { get; set; }

    /// <summary>This tab's own recall history for its send field/box — independent of every other open tab's, per docs/design/multi-session-ui.md's Open questions (recommended per-tab, not shared).</summary>
    public SendHistory SendHistory { get; } = new();

    /// <summary>Builds a new tab from a fresh <see cref="DevTermSessionBuilder.Build"/> result — the shape a live profile switch, or a future "File &gt; New Session", uses.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="cliOptions"/> names an unknown presenter, or a parser that can't encode input.</exception>
    public static SessionTab Build(CliOptions cliOptions)
    {
        var built = DevTermSessionBuilder.Build(cliOptions);
        return new SessionTab(built.Session, built.Catalog, cliOptions, built.Services);
    }

    /// <summary>This tab's window/tab-label title, e.g. <c>dev-term — tek2230 (ascii; send as ascii)</c> — see <see cref="ConnectionDescription.WindowTitle"/>.</summary>
    public string Title(ConnectionProfileStore profileStore) =>
        ConnectionDescription.WindowTitle(CliOptions, Parser, profileStore, Session.State == ConnectionState.Open);

    /// <summary>
    /// Called when this tab's routing is about to deliver a confirm-flagged broker message to the device. A front end sets it to
    /// show its prompt; unset, such messages are dropped rather than sent unasked. See docs/specs/routing-window.md.
    /// </summary>
    public Func<RoutingRule, string, Task<RoutingConfirmChoice>>? RoutingConfirm { get; set; }

    /// <summary>Overrides how broker links are built (tests); null uses the real MQTT/AMQP/STOMP bridges.</summary>
    public IRoutingLinkFactory? RoutingLinkFactory { get; set; }

    /// <summary>Raised when <see cref="Routing"/> is created or replaced, so an open Routing window can rebind.</summary>
    public event EventHandler? RoutingChanged;

    /// <summary>The routing service for this tab's profile (<see cref="CliOptions.Routing"/>); started by itself whenever the session opens and the profile has rules.</summary>
    public RoutingService Routing => _routing ??= new RoutingService(CliOptions.Routing, RoutingLinkFactory);

    /// <summary>True when the profile has a broker and rules, so connecting should start routing.</summary>
    public bool HasRouting => CliOptions.Routing.IsConfigured;

    /// <summary>Starts routing on the current session now (the Routing window's Start); a no-op when already running.</summary>
    public void StartRouting() => Routing.Start(Session, CliOptions.LineEnding.ToChars(), RoutingConfirm);

    /// <summary>Replaces the routing service after the profile's routing section changed (the window's Apply): stops the old one, and starts the new one if the session is open.</summary>
    public async Task ApplyRoutingAsync(RoutingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_routing is not null)
        {
            await _routing.StopAsync().ConfigureAwait(false);
        }

        CliOptions.Routing = options;
        _routing = new RoutingService(options, RoutingLinkFactory);
        RoutingChanged?.Invoke(this, EventArgs.Empty);
        if (Session.State == Core.Transports.ConnectionState.Open && options.IsConfigured)
        {
            StartRouting();
        }
    }

    private sealed class RoutingObserver(SessionTab owner) : ISessionObserver
    {
        public void OnOpened()
        {
            if (owner.HasRouting)
            {
                owner.StartRouting();
            }
        }

        public void OnReceived(ReadOnlySequence<byte> data)
        {
        }

        public void OnSent(ReadOnlyMemory<byte> data)
        {
        }

        public void OnClosed(bool requested, Exception? error)
        {
            if (owner._routing is { } routing)
            {
                _ = Task.Run(routing.StopAsync);
            }
        }
    }
}
