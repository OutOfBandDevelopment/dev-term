using DevTerm.Core.Presenters;
using DevTerm.Logging.Playback;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Configuration;

/// <summary>
/// The presenter catalog playback replays through — the same presenters every front end offers
/// live, composed via <see cref="ServiceCollectionExtensions.AddDevTermPresenters"/> with no
/// transport registered at all, so playback can't open a connection even by mistake. Each
/// <see cref="CreatePipeline"/> resolves a fresh catalog, and presenters are transient, so every
/// rewind starts from brand-new presenter state.
/// </summary>
public sealed class PlaybackPresenters
{
    private readonly IServiceProvider _provider;

    public PlaybackPresenters(CliOptions? cliOptions = null)
    {
        var services = new ServiceCollection();
        services.AddDevTermPresenters(cliOptions ?? new CliOptions());
        _provider = services.BuildServiceProvider();
        Names = [.. _provider.GetRequiredService<PresenterCatalog>().Names];
    }

    /// <summary>Every presenter name playback can use.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <exception cref="KeyNotFoundException">A name isn't a registered presenter.</exception>
    public Pipeline CreatePipeline(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var catalog = _provider.GetRequiredService<PresenterCatalog>();
        return new Pipeline(names.Select(catalog.Get));
    }

    /// <summary>Loads <paramref name="path"/> for playback through these presenters.</summary>
    public PlaybackController Open(string path, TimeProvider? clock = null) =>
        PlaybackController.Open(path, CreatePipeline, Names, clock);
}
