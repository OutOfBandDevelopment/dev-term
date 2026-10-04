using DevTerm.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.K8055;

/// <summary>Loads the K8055 presenter and panel through the plugin loader (plugins/k8055), so the core projects don't reference this device.</summary>
public sealed class K8055PluginModule : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddK8055Presenter();
}
