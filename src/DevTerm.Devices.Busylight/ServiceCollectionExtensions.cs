using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.Busylight;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="BusylightDecoder"/> as an ordinary <see cref="IPresenter"/> — mirrors <c>DevTerm.Presenters.Text</c>'s <c>AddTextPresenters</c>, so it's selectable via <c>--presenter busylight</c> and shows up in <c>PresenterCatalog</c> like any other presenter.</summary>
    public static IServiceCollection AddBusylightPresenter(this IServiceCollection services)
    {
        services.AddTransient<IPresenter, BusylightDecoder>();
        return services;
    }
}
