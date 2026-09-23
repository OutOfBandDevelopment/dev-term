using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.Scpi;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ScpiReplyPresenter"/> as an ordinary <see cref="IPresenter"/> — mirrors <c>DevTerm.Devices.K8055</c>'s <c>AddK8055Presenter</c>, so it's selectable via <c>--presenter scpi</c> and shows up in <c>PresenterCatalog</c> like any other presenter.</summary>
    public static IServiceCollection AddScpiPresenter(this IServiceCollection services)
    {
        services.AddSingleton<IPresenter, ScpiReplyPresenter>();
        return services;
    }
}
