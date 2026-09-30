using DevTerm.Core.Presenters;
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

        Session = session;
        Catalog = catalog;
        CliOptions = cliOptions;
        Services = services;
        Parser = cliOptions.EffectiveParser;
    }

    /// <summary>Reassigned, not replaced-by-a-new-tab, on a live profile switch — mirrors today's <c>TuiMode</c>/<c>MainWindow</c> field reassignment exactly.</summary>
    public Session Session { get; set; }

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
}
