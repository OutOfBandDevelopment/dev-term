using DevTerm.Core.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Usbtmc;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the USBTMC transport and its options. Callers still need to bind/configure
    /// <see cref="UsbtmcTransportOptions"/> (vendor/product ID, ...) themselves.
    /// </summary>
    public static IServiceCollection AddUsbtmcTransport(this IServiceCollection services)
    {
        services.AddOptions<UsbtmcTransportOptions>().ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IUsbtmcDeviceFactory, SystemUsbtmcDeviceFactory>();
        services.AddSingleton<IUsbtmcDeviceDiscovery, SystemUsbtmcDeviceDiscovery>();
        services.AddTransient<ITransport, UsbtmcTransport>();
        return services;
    }
}
