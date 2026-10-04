using DevTerm.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.Busylight;

/// <summary>Loads the Busylight presenter and panel through the plugin loader (plugins/busylight), so the core projects don't reference this device.</summary>
public sealed class BusylightPluginModule : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddBusylightPresenter();
}
