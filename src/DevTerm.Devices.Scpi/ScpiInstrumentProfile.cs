namespace DevTerm.Devices.Scpi;

/// <summary>
/// A named, per-instrument-family command set — the "sub device profile" concept from
/// docs/design/proposals/scpi-instrument-control.md. Loaded from JSON by <see cref="ScpiProfileCatalog"/>,
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

    public List<ScpiCommandDefinition> Commands { get; set; } = [];
}
