using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.RadexOne;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="RadexOneDecoder"/> as an ordinary <see cref="IPresenter"/> — mirrors <c>DevTerm.Presenters.Text</c>'s <c>AddTextPresenters</c>, so it's selectable via <c>--presenter radexone</c> and shows up in <c>PresenterCatalog</c> like any other presenter.</summary>
    public static IServiceCollection AddRadexOnePresenter(this IServiceCollection services)
    {
        services.AddTransient<IPresenter, RadexOneDecoder>();
        return services;
    }
}
