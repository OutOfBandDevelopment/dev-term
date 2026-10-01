using DevTerm.Core.Transports;
using DevTerm.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevTerm.Transports.Rfc2217;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RFC 2217 transport and its options. Callers still need to bind/configure
    /// <see cref="Rfc2217TransportOptions"/> (host, port, baud, ...) themselves.
    /// </summary>
    public static IServiceCollection AddRfc2217Transport(this IServiceCollection services)
    {
        services.AddOptions<Rfc2217TransportOptions>().ValidateDataAnnotations().ValidateOnStart();

        // TryAdd: idempotent if DevTerm.Transports.Tcp's own AddTcpTransport() already registered this.
        services.TryAddSingleton<ITcpConnectionSource, SystemTcpConnectionSource>();
        services.AddTransient<ITransport, Rfc2217Transport>();
        return services;
    }
}
