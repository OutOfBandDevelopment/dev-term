using DevTerm.Core.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Serial;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the serial transport and its options. Callers still need to bind/configure
    /// <see cref="SerialTransportOptions"/> (port name, baud rate, ...) themselves.
    /// </summary>
    public static IServiceCollection AddSerialTransport(this IServiceCollection services)
    {
        services.AddOptions<SerialTransportOptions>().ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ISerialPortFactory, SystemSerialPortFactory>();
        services.AddSingleton<ISerialPortDiscovery, SystemSerialPortDiscovery>();
        services.AddTransient<ITransport, SerialTransport>();
        return services;
    }
}
