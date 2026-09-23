using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.K8055;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="K8055Decoder"/> as an ordinary <see cref="IPresenter"/> — mirrors <c>DevTerm.Presenters.Text</c>'s <c>AddTextPresenters</c>, so it's selectable via <c>--presenter k8055</c> and shows up in <c>PresenterCatalog</c> like any other presenter.</summary>
    public static IServiceCollection AddK8055Presenter(this IServiceCollection services)
    {
        services.AddSingleton<IPresenter, K8055Decoder>();
        return services;
    }
}
