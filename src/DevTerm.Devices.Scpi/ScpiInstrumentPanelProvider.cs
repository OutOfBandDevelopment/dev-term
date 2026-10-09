using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// SCPI as an <see cref="IInstrumentPanelProvider"/>: one Device-menu item, a picker over <see cref="ScpiProfileCatalog"/> plus the
/// auto-detect and generic choices, and a panel built from the chosen profile's data. Loaded through <see cref="ScpiPluginModule"/>,
/// so the core and front-end projects do not reference this project.
/// </summary>
public sealed class ScpiInstrumentPanelProvider : IInstrumentPanelProvider
{
    public string Id => "scpi";

    public string MenuTitle => "_SCPI Instrument...";

    public string PickerTitle => "Select SCPI Instrument";

    public string PresenterName => "scpi";

    public string AutoDetectChoice => ScpiProfileCatalog.AutoDetectChoiceName;

    public string GenericChoice => ScpiProfileCatalog.Generic.Name;

    public IReadOnlyList<string> ProfileNames => [.. ScpiProfileCatalog.All.Select(p => p.Name)];

    // SCPI is text over a byte stream: any transport but HID (fixed-size binary reports).
    public bool IsAvailable(string transport, int vendorId, int productId) =>
        !string.Equals(transport, "hid", StringComparison.OrdinalIgnoreCase);

    public bool ConfigureForProfile(string profileName, IPresenter? presenter)
    {
        if (ScpiProfileCatalog.All.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)) is not { } profile)
        {
            return false;
        }

        if (presenter is ScpiReplyPresenter reply)
        {
            reply.ConfigureTerminator(profile.Terminator);
        }

        return true;
    }

    public InstrumentPanel Open(string choice, Session session, IPresenter? structuredSource)
    {
        ArgumentNullException.ThrowIfNull(session);
        var profile = choice == GenericChoice
            ? ScpiProfileCatalog.Generic
            : ScpiProfileCatalog.All.First(p => p.Name == choice);
        if (structuredSource is ScpiReplyPresenter reply)
        {
            reply.ConfigureTerminator(profile.Terminator);
        }

        return new InstrumentPanel(
            ScpiUiDefinitionBuilder.Build(profile),
            new ScpiControlSurface(session, profile, structuredSource as IScpiReplyTracker),
            $"dev-term — {profile.Name}");
    }

    public string DetectProgressMessage(TimeSpan timeout) => ScpiAutoDetect.ProgressMessage(timeout);

    public async Task<InstrumentDetection> DetectAsync(Session session, IPresenter? structuredSource, TimeSpan timeout)
    {
        var result = await ScpiAutoDetect.DetectAsync(session, structuredSource, timeout).ConfigureAwait(false);
        return new InstrumentDetection(result.Profile?.Name ?? GenericChoice, result.Describe(timeout));
    }
}
