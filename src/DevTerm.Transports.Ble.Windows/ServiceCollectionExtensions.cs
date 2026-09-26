using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Transports.Ble.Windows;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The entry point <c>DevTerm.Configuration.BlePlatformAdapterLoader</c> looks up by name via
    /// reflection and invokes after <c>DevTerm.Transports.Ble.ServiceCollectionExtensions.AddBleTransport</c>,
    /// overriding its default "unsupported platform" registrations with these real
    /// Windows.Devices.Bluetooth-backed ones. Never called directly from a project reference - see
    /// this assembly's csproj comment for why.
    /// </summary>
    public static IServiceCollection AddWindowsBleAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IBleAdapterFactory, WindowsBleAdapterFactory>();
        services.AddSingleton<IBleDeviceDiscovery, WindowsBleDeviceDiscovery>();
        return services;
    }
}
