using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Presenters.Text;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in text and numeric-base presenters (ASCII, UTF-8, hex, decimal,
    /// octal, binary). See docs/design/presenters.md.
    /// </summary>
    public static IServiceCollection AddTextPresenters(this IServiceCollection services)
    {
        services.AddSingleton<IPresenter, AsciiPresenter>();
        services.AddSingleton<IPresenter, Utf8Presenter>();
        services.AddSingleton<IPresenter, HexPresenter>();
        services.AddSingleton<IPresenter, DecimalPresenter>();
        services.AddSingleton<IPresenter, OctalPresenter>();
        services.AddSingleton<IPresenter, BinaryPresenter>();
        return services;
    }
}
