using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.ZoomH4n;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ZoomH4nDecoder"/> as an ordinary <see cref="IPresenter"/> — mirrors <c>DevTerm.Presenters.Text</c>'s <c>AddTextPresenters</c>, so it's selectable via <c>--presenter zoomh4n</c> and shows up in <c>PresenterCatalog</c> like any other presenter.</summary>
    public static IServiceCollection AddZoomH4nPresenter(this IServiceCollection services)
    {
        services.AddTransient<IPresenter, ZoomH4nDecoder>();
        return services;
    }
}
