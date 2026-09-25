using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Core.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core engine services (presenter catalog, session factory) that every
    /// front end composes alongside whatever transport/presenter plugins it adds.
    /// </summary>
    public static IServiceCollection AddDevTermCore(this IServiceCollection services)
    {
        // Transient, like every IPresenter registration: presenters are stateful (the ASCII
        // presenter's line buffer, the SCPI presenter's pending-query queue), so each resolved
        // catalog - one per session - builds its own instances instead of every session in the
        // process sharing, and interleaving partial frames through, the same ones. Resolve the
        // catalog once per session and pass that instance around; resolving it again gets a
        // different, unconnected set of presenters.
        services.AddTransient<PresenterCatalog>();
        services.AddSingleton<ISessionFactory, SessionFactory>();
        return services;
    }
}
