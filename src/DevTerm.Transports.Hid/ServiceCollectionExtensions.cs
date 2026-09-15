using DevTerm.Core.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Hid;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the USB HID transport and its options. Callers still need to bind/configure
    /// <see cref="HidTransportOptions"/> (vendor/product ID, ...) themselves.
    /// </summary>
    public static IServiceCollection AddHidTransport(this IServiceCollection services)
    {
        services.AddOptions<HidTransportOptions>().ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IHidDeviceFactory, SystemHidDeviceFactory>();
        services.AddSingleton<IHidDeviceDiscovery, SystemHidDeviceDiscovery>();
        services.AddTransient<ITransport, HidTransport>();
        return services;
    }
}
