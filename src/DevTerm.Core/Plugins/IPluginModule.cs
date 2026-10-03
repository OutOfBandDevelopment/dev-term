using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Core.Plugins;

/// <summary>
/// The entry point of a plugin assembly: registers whatever it contributes (<c>IPresenter</c>s, an <c>ITransport</c>
/// and its options) the same way the built-in <c>AddXyz</c> extensions do. See docs/design/plugin-model.md.
/// </summary>
public interface IPluginModule
{
    void ConfigureServices(IServiceCollection services);
}
