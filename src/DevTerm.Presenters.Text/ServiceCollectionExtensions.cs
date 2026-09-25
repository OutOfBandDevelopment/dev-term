using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Presenters.Text;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in text and numeric-base presenters (ASCII, UTF-8, hex, decimal,
    /// octal, binary). See docs/design/presenters.md. Callers can configure
    /// <see cref="AsciiPresenterOptions"/> (e.g. via <c>services.Configure&lt;AsciiPresenterOptions&gt;(...)</c>)
    /// to set <see cref="AsciiPresenterOptions.MaxLineLength"/>.
    /// </summary>
    public static IServiceCollection AddTextPresenters(this IServiceCollection services)
    {
        services.AddOptions<AsciiPresenterOptions>().ValidateDataAnnotations().ValidateOnStart();
        services.AddTransient<IPresenter, AsciiPresenter>();
        services.AddTransient<IPresenter, Utf8Presenter>();
        services.AddTransient<IPresenter, HexPresenter>();
        services.AddTransient<IPresenter, DecimalPresenter>();
        services.AddTransient<IPresenter, OctalPresenter>();
        services.AddTransient<IPresenter, BinaryPresenter>();
        return services;
    }
}
