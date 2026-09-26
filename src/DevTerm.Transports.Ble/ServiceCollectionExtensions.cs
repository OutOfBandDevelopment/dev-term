using DevTerm.Core.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevTerm.Transports.Ble;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BLE transport and its options, plus a default "no adapter available"
    /// <see cref="IBleAdapterFactory"/>/<see cref="IBleDeviceDiscovery"/> pair. A per-OS backend
    /// (e.g. <c>DevTerm.Transports.Ble.Windows</c>) overrides these with a real implementation by
    /// registering its own after this call - the last registration wins when a service is resolved
    /// as a single instance, so callers must call this first. Callers still need to bind/configure
    /// <see cref="BleTransportOptions"/> (device id, ...) themselves.
    /// </summary>
    public static IServiceCollection AddBleTransport(this IServiceCollection services)
    {
        services.AddOptions<BleTransportOptions>().ValidateDataAnnotations().ValidateOnStart();
        services.TryAddSingleton<IBleAdapterFactory, UnsupportedPlatformBleAdapterFactory>();
        services.TryAddSingleton<IBleDeviceDiscovery, UnsupportedPlatformBleDeviceDiscovery>();
        services.AddTransient<ITransport, BleTransport>();
        return services;
    }
}
