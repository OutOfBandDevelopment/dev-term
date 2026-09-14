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
        services.AddSingleton<PresenterCatalog>();
        services.AddSingleton<ISessionFactory, SessionFactory>();
        return services;
    }
}
