using DevTerm.Core.Transports;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevTerm.Transports.Vxi11;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the VXI-11 transport and its options. Callers still need to bind/configure
    /// <see cref="Vxi11TransportOptions"/> (host, device, ...) themselves.
    /// </summary>
    public static IServiceCollection AddVxi11Transport(this IServiceCollection services)
    {
        services.AddOptions<Vxi11TransportOptions>().ValidateDataAnnotations().ValidateOnStart();

        // TryAdd: idempotent if DevTerm.Transports.Tcp's own AddTcpTransport() already registered this.
        services.TryAddSingleton<ITcpConnectionSource, SystemTcpConnectionSource>();
        services.AddTransient<ITransport, Vxi11Transport>();
        return services;
    }
}
