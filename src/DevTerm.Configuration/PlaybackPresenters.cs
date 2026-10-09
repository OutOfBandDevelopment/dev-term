using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Logging;
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
    public Pipeline CreatePipeline(IReadOnlyList<string> names) => CreatePipeline(names, null);

    /// <summary>
    /// A pipeline of fresh presenters; when <paramref name="instrumentProfile"/> names a SCPI instrument profile (the log header's
    /// <see cref="SessionLogHeader.ScpiProfile"/>) the owning presenter is configured for it, so a terminatorless instrument's
    /// replies flush on playback as they did live.
    /// </summary>
    public Pipeline CreatePipeline(IReadOnlyList<string> names, string? instrumentProfile)
    {
        ArgumentNullException.ThrowIfNull(names);
        var catalog = _provider.GetRequiredService<PresenterCatalog>();
        if (instrumentProfile is { Length: > 0 })
        {
            foreach (var provider in InstrumentPanelProviders.All)
            {
                if (catalog.TryGet(provider.PresenterName, out var presenter) && provider.ConfigureForProfile(instrumentProfile, presenter))
                {
                    break;
                }
            }
        }

        return new Pipeline(names.Select(catalog.Get));
    }

    /// <summary>The SCPI profile a log was captured with: the header's, else the first one picked from a Device panel mid-session (an <c>instrument</c> record).</summary>
    public static string? InstrumentProfileOf(SessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.Header.ScpiProfile ?? log.Records.FirstOrDefault(r => r.Kind == SessionLogRecordKind.Instrument)?.Profile;
    }

    /// <summary>Loads <paramref name="path"/> for playback through these presenters, applying the log's recorded SCPI profile.</summary>
    public PlaybackController Open(string path, TimeProvider? clock = null)
    {
        var log = SessionLog.OpenIndexed(path);
        return new PlaybackController(path, log, names => CreatePipeline(names, InstrumentProfileOf(log)), Names, clock);
    }
}
