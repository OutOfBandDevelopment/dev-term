using DevTerm.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.De5000;

/// <summary>Loads the De5000 presenter and panel through the plugin loader (plugins/de5000), so the core projects don't reference this device.</summary>
public sealed class De5000PluginModule : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddDe5000Presenter();
}
