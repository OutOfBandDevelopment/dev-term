using DevTerm.Core.StreamContent;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// One outbound SCPI (or SCPI-adjacent, e.g. Korad's plain-ASCII protocol) command, declared as
/// data rather than code — see docs/design/features/scpi-instrument-control.md.
/// </summary>
public sealed class ScpiCommandDefinition
{
    public required string Id { get; set; }

    public required string Label { get; set; }

    /// <summary>Groups related commands into one <c>UiSection</c> (e.g. "Measure", "Source", "Output").</summary>
    public string Category { get; set; } = "Commands";

    /// <summary>
    /// The command text, with <c>{Name}</c> tokens substituted from <see cref="Parameters"/> by
    /// name (e.g. <c>"SOUR1:FREQ {Frequency}"</c>, <c>"*IDN?"</c>). No terminator here —
    /// <see cref="ScpiInstrumentProfile.Terminator"/> is appended once at send time.
    /// </summary>
    public required string Template { get; set; }

    /// <summary>
    /// Whether this command expects a reply line — if true, the renderer adds a matching
    /// <c>IndicatorControl</c> (<c>{Id}.reply</c>) and <see cref="ScpiControlSurface"/> registers
    /// the pending reply with the active <see cref="IScpiReplyTracker"/> before sending.
    /// </summary>
    public bool IsQuery { get; set; }

    /// <summary>
    /// What this command's reply contains, when it isn't an ordinary text line — e.g. <c>Image</c>
    /// for a screen-dump query that returns a bitmap in a definite-length block. When set to
    /// anything but <see cref="StreamContentFormat.Text"/> (the default), <see cref="ScpiControlSurface"/>
    /// tells a running Stream Monitor to expect it just before sending, so the reply is captured and
    /// saved as a file instead of the monitor having to recognize it from its bytes alone (see
    /// docs/design/proposals/stream-content-detection.md). Declared per command rather than per
    /// profile because it's a property of the command — a command whose output format is itself
    /// configurable (a hard copy whose format is set by another command) is better left to sniffing.
    /// </summary>
    public StreamContentFormat ExpectedResponseFormat { get; set; } = StreamContentFormat.Text;

    public List<ScpiParameterDefinition> Parameters { get; set; } = [];
}
