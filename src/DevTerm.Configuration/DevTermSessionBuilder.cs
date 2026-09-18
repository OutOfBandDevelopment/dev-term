using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Configuration;

/// <summary>
/// Builds a real, independent <see cref="Session"/> from a <see cref="CliOptions"/>, via its own
/// small throwaway <see cref="IServiceProvider"/> composed the same way
/// <see cref="ServiceCollectionExtensions.AddDevTermFrontEnd"/> wires the app's main host. Used for
/// live mid-session profile switching (TUI/WPF "File &gt; Device Profiles..."), where a fresh
/// transport needs composing from a newly-picked profile without touching — or being able to
/// touch — the app's own long-lived host, which was already built once at startup from the
/// *original* <see cref="CliOptions"/> and has no API for re-registering a transport into it.
/// </summary>
public static class DevTermSessionBuilder
{
    /// <param name="Session">A real, unopened <see cref="Sessions.Session"/> — call <c>OpenAsync</c> to connect it.</param>
    /// <param name="Catalog">
    /// Every registered presenter, so a front end can resolve <see cref="CliOptions.EffectiveParser"/>
    /// (and any other send format the user switches to mid-session) to an <see cref="IPresenterInput"/>.
    /// </param>
    /// <param name="Services">
    /// The throwaway provider that composed <paramref name="Session"/>'s transport. Deliberately
    /// never disposed by this method or required to be disposed by the caller: <see cref="Session.DisposeAsync"/>
    /// already disposes the transport instance it owns directly, and every transport's own
    /// <c>CloseAsync</c>/<c>DisposeAsync</c> guards against being called twice (an already-closed
    /// check, verified for all three built-in transports) — so there's nothing left for this
    /// provider's own disposal to usefully do, and skipping it avoids depending on that
    /// already-closed guard holding for every future transport too. The provider itself holds no
    /// unmanaged resources of its own; letting it become unreachable is enough.
    /// </param>
    public sealed record Result(Session Session, PresenterCatalog Catalog, IServiceProvider Services);

    /// <exception cref="InvalidOperationException"><paramref name="cliOptions"/> names an unknown presenter, or a parser that can't encode input.</exception>
    public static Result Build(CliOptions cliOptions)
    {
        var services = new ServiceCollection();
        services.AddDevTermFrontEnd(cliOptions);
        var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<PresenterCatalog>();
        var presenters = ResolvePresenters(catalog, cliOptions);

        var transport = provider.GetRequiredService<ITransport>();
        var sessionFactory = provider.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.Create(transport, new Pipeline(presenters));

        return new Result(session, catalog, provider);
    }

    /// <summary>
    /// Resolves <see cref="CliOptions.EffectivePresenters"/> to the registered presenters (in the
    /// order listed) and checks <see cref="CliOptions.EffectiveParser"/> can encode typed input.
    /// Shared by every entry point that composes a session so a bad name fails the same way
    /// everywhere.
    /// </summary>
    /// <exception cref="InvalidOperationException">A presenter name isn't registered, or the parser isn't a presenter that can encode input.</exception>
    public static IReadOnlyList<IPresenter> ResolvePresenters(PresenterCatalog catalog, CliOptions cliOptions)
    {
        var presenters = new List<IPresenter>();
        foreach (var name in cliOptions.EffectivePresenters)
        {
            if (!catalog.TryGet(name, out var presenter))
            {
                throw new InvalidOperationException(
                    $"Unknown presenter '{name}'. Available: {string.Join(", ", catalog.Names)}");
            }

            presenters.Add(presenter);
        }

        if (!catalog.TryGetInput(cliOptions.EffectiveParser, out _))
        {
            throw new InvalidOperationException(
                $"Unknown parser '{cliOptions.EffectiveParser}'. Available: {string.Join(", ", catalog.InputNames)}");
        }

        return presenters;
    }
}
