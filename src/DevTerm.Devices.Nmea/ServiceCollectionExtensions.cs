using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.Nmea;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="NmeaGpsDecoder"/> as an ordinary <see cref="IPresenter"/> - mirrors <c>DevTerm.Presenters.Text</c>'s <c>AddTextPresenters</c>, so it's selectable via <c>--presenter nmea</c> and shows up in <c>PresenterCatalog</c> like any other presenter.</summary>
    public static IServiceCollection AddNmeaGpsPresenter(this IServiceCollection services)
    {
        services.AddTransient<IPresenter, NmeaGpsDecoder>();
        return services;
    }
}
