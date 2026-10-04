using DevTerm.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.Nmea;

/// <summary>Loads the Nmea presenter and panel through the plugin loader (plugins/nmea), so the core projects don't reference this device.</summary>
public sealed class NmeaPluginModule : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddNmeaGpsPresenter();
}
