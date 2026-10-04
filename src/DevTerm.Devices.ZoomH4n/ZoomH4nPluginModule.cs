using DevTerm.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.ZoomH4n;

/// <summary>Loads the ZoomH4n presenter and panel through the plugin loader (plugins/zoomh4n), so the core projects don't reference this device.</summary>
public sealed class ZoomH4nPluginModule : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddZoomH4nPresenter();
}
