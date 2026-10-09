using DevTerm.Core.Control;
using DevTerm.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.Scpi;

/// <summary>Loads the scpi presenter and the SCPI instrument panel through the plugin loader (plugins/scpi), so the core projects do not reference this project.</summary>
public sealed class ScpiPluginModule : IPluginModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScpiPresenter();
        services.AddSingleton<IInstrumentPanelProvider, ScpiInstrumentPanelProvider>();
    }
}
