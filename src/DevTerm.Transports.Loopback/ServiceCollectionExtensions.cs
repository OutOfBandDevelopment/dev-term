using DevTerm.Core.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Loopback;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the loopback transport and its (currently empty) options.</summary>
    public static IServiceCollection AddLoopbackTransport(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<LoopbackTransportOptions>, LoopbackTransportOptionsValidator>();
        services.AddOptions<LoopbackTransportOptions>().ValidateOnStart();
        services.AddTransient<ITransport, LoopbackTransport>();
        return services;
    }
}
