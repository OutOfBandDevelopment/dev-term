using DevTerm.Core.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevTerm.Transports.Mqtt;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the MQTT transport. Callers still configure <see cref="MqttTransportOptions"/> themselves.</summary>
    public static IServiceCollection AddMqttTransport(this IServiceCollection services)
    {
        services.AddOptions<MqttTransportOptions>().ValidateDataAnnotations().ValidateOnStart();
        services.TryAddSingleton<IMqttConnectionFactory, MqttNetConnectionFactory>();
        services.AddTransient<ITransport, MqttTransport>();
        return services;
    }
}
