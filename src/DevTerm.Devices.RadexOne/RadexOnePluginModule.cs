using DevTerm.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.RadexOne;

/// <summary>Loads the RadexOne presenter and panel through the plugin loader (plugins/radexone), so the core projects don't reference this device.</summary>
public sealed class RadexOnePluginModule : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddRadexOnePresenter();
}
