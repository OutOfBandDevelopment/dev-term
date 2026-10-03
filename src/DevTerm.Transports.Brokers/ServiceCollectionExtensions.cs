using DevTerm.Core.Transports;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Transports.Brokers;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the AMQP 0-9-1 transport. Callers still configure <see cref="BrokerTransportOptions"/> themselves.</summary>
    public static IServiceCollection AddAmqpTransport(this IServiceCollection services) => Add(services, new AmqpConnectionFactory());

    /// <summary>Registers the STOMP 1.2 transport. Callers still configure <see cref="BrokerTransportOptions"/> themselves.</summary>
    public static IServiceCollection AddStompTransport(this IServiceCollection services) => Add(services, new StompConnectionFactory());

    private static IServiceCollection Add(IServiceCollection services, IBrokerConnectionFactory factory)
    {
        services.AddOptions<BrokerTransportOptions>().ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(factory);
        services.AddTransient<ITransport, BrokerTransport>();
        return services;
    }
}
