namespace DevTerm.Devices.Scpi;

/// <summary>
/// A named, per-instrument-family command set — the "sub device profile" concept from
/// docs/design/features/scpi-instrument-control.md. Loaded from JSON by <see cref="ScpiProfileCatalog"/>,
/// not hardcoded per device, so adding support for another instrument is a new file, not new code.
/// </summary>
public sealed class ScpiInstrumentProfile
{
    public required string Name { get; set; }

    /// <summary>
    /// A regex matched against a <c>*IDN?</c> reply to auto-select this profile. Null/empty for a
    /// profile that's never auto-selected (e.g. the built-in Generic baseline).
    /// </summary>
    public string? IdnPattern { get; set; }

    /// <summary>Appended to every command sent through this profile. Most SCPI gear wants <c>"\n"</c>; some want <c>"\r\n"</c> or bare <c>"\r"</c>.</summary>
    public string Terminator { get; set; } = "\n";

    /// <summary>
    /// Free-text operational knowledge worth surfacing alongside this profile's command set —
    /// required non-default connection settings, a mandatory preamble command, quirks found only by
    /// testing against real hardware. Folded into <see cref="UiDefinitions.UiDefinition.Description"/>
    /// by <see cref="ScpiUiDefinitionBuilder.Build"/>, shown by both control-panel renderers. Null when
    /// there's nothing worth noting.
    /// </summary>
    public string? Notes { get; set; }

    public List<ScpiCommandDefinition> Commands { get; set; } = [];
}
