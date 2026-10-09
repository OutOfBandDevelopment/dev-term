using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Core.Control;

/// <summary>A control panel an <see cref="IInstrumentPanelProvider"/> opened for one chosen instrument.</summary>
public sealed record InstrumentPanel(UiDefinition Definition, IControlSurface Surface, string Title);

/// <summary>What an auto-detect settled on: the <paramref name="Choice"/> to open (an instrument name, or the generic one) and a one-line <paramref name="Message"/> for the output pane.</summary>
public sealed record InstrumentDetection(string Choice, string Message);

/// <summary>
/// A family of instruments whose panel is picked from data rather than hand-coded per device (SCPI is the one today): the
/// Device menu gets one item, which asks which instrument (or auto-detects it), then opens that instrument's panel. A plugin
/// registers one next to its presenter; the front ends never name the plugin's project. See
/// docs/design/proposals/plugin-contributed-panels.md. For a fixed single-device panel use <see cref="IDevicePanelContribution"/>.
/// </summary>
public interface IInstrumentPanelProvider
{
    /// <summary>A stable id, e.g. "scpi".</summary>
    string Id { get; }

    /// <summary>The Device menu text, with an <c>_</c> before the access key, e.g. "_SCPI Instrument...".</summary>
    string MenuTitle { get; }

    /// <summary>The title of the instrument picker.</summary>
    string PickerTitle { get; }

    /// <summary>The structured presenter that correlates replies for the panel and feeds its indicators.</summary>
    string PresenterName { get; }

    /// <summary>The picker choice that asks the device which instrument it is.</summary>
    string AutoDetectChoice { get; }

    /// <summary>The picker choice for an instrument with no profile of its own.</summary>
    string GenericChoice { get; }

    /// <summary>The names of the instruments with a profile, in picker order (excluding the auto-detect and generic choices).</summary>
    IReadOnlyList<string> ProfileNames { get; }

    /// <summary>Whether the panel makes sense for a connection (the menu item is disabled otherwise). Called only while connected.</summary>
    bool IsAvailable(string transport, int vendorId, int productId);

    /// <summary>
    /// Applies what a named profile says about the wire (its reply terminator) to <paramref name="presenter"/> without opening a panel,
    /// so a scripted session of a terminatorless instrument does not wait for a line ending that never comes. False when no profile has that name.
    /// </summary>
    bool ConfigureForProfile(string profileName, IPresenter? presenter);

    /// <summary>Opens the panel for <paramref name="choice"/> (a profile name or <see cref="GenericChoice"/>).</summary>
    InstrumentPanel Open(string choice, Session session, IPresenter? structuredSource);

    /// <summary>The line shown while <see cref="DetectAsync"/> waits.</summary>
    string DetectProgressMessage(TimeSpan timeout);

    /// <summary>Asks the device which instrument it is. A send failure propagates (the session has then closed itself and reported why).</summary>
    Task<InstrumentDetection> DetectAsync(Session session, IPresenter? structuredSource, TimeSpan timeout);
}

/// <summary>Helpers over <see cref="IInstrumentPanelProvider"/> that every front end shares.</summary>
public static class InstrumentPanelProviderExtensions
{
    /// <summary>Whether <paramref name="choice"/> is something the provider's picker offers (the two synthetic choices or a profile name).</summary>
    public static bool Offers(this IInstrumentPanelProvider provider, string? choice)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return !string.IsNullOrWhiteSpace(choice)
            && (choice == provider.AutoDetectChoice || choice == provider.GenericChoice || provider.ProfileNames.Contains(choice));
    }

    /// <summary>The picker's rows: the auto-detect choice, the generic one, then each profile.</summary>
    public static IReadOnlyList<string> PickerChoices(this IInstrumentPanelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return [provider.AutoDetectChoice, provider.GenericChoice, .. provider.ProfileNames];
    }
}

/// <summary>
/// The instrument providers the loaded plugins registered, for the places that run before or outside a built host (the
/// Connection Editor lists their profile names). Filled by <c>AddPlugins</c>; empty until then.
/// </summary>
public static class InstrumentPanelProviders
{
    private static IReadOnlyList<IInstrumentPanelProvider> _all = [];

    public static IReadOnlyList<IInstrumentPanelProvider> All => _all;

    public static void Set(IEnumerable<IInstrumentPanelProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _all = [.. providers];
    }

    /// <summary>The picker rows for every provider's profiles, with the synthetic choices once, for a "which instrument" setting.</summary>
    public static IReadOnlyList<string> ProfileChoices() =>
        [.. _all.SelectMany(p => p.PickerChoices()).Distinct(StringComparer.Ordinal)];
}
