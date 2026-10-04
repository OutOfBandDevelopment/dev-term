using DevTerm.Core.Control;
using DevTerm.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.Demo;

/// <summary>An example plugin: contributes a control panel for the loopback device (plugins/demo).</summary>
public sealed class DemoPluginModule : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddSingleton<IDevicePanelContribution, DemoPanelContribution>();
}
