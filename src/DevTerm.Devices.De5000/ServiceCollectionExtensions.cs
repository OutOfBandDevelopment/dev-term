using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.De5000;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="De5000Decoder"/> as an ordinary <see cref="IPresenter"/> - mirrors <c>DevTerm.Presenters.Text</c>'s <c>AddTextPresenters</c>, so it's selectable via <c>--presenter de5000</c> and shows up in <c>PresenterCatalog</c> like any other presenter.</summary>
    public static IServiceCollection AddDe5000Presenter(this IServiceCollection services)
    {
        services.AddTransient<IPresenter, De5000Decoder>();
        return services;
    }
}
