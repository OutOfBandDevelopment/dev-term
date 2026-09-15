using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>
/// A complete, no-code description of a device: identity, a transport hint, the declarative
/// command/response schema from docs/design/device-control-modules.md, and a control-panel
/// <see cref="UiDefinition"/> — either inline or loaded from <see cref="UiFile"/> by
/// <see cref="DeviceManifestLoader"/>. See docs/design/device-manifests.md.
/// </summary>
public sealed class DeviceManifest
{
    public required string Name { get; set; }

    public string? Vendor { get; set; }

    public string? Version { get; set; }

    public string? Description { get; set; }

    public TransportHint? Transport { get; set; }

    public InboundProtocol? Inbound { get; set; }

    public List<OutboundCommand> OutboundCommands { get; set; } = [];

    /// <summary>Inline UI definition (single-file mode). If null, <see cref="DeviceManifestLoader"/> loads it from <see cref="UiFile"/> instead.</summary>
    public UiDefinition? Ui { get; set; }

    /// <summary>Path to an external UI definition file (.json or .xml), relative to the manifest's own location (package mode).</summary>
    public string? UiFile { get; set; }
}

/// <summary>Loose key/value defaults for pre-filling connection setup (e.g. Type="serial", Options=[{Key:"Baud",Value:"9600"}]) — a hint, not a hard requirement.</summary>
public sealed class TransportHint
{
    public required string Type { get; set; }

    // A List<TransportOption>, not a Dictionary<string,string>: XmlSerializer has no native
    // support for IDictionary (throws NotSupportedException at type-reflection time), while a
    // plain list of a simple Key/Value class works natively for both JSON and XML with no
    // special-casing either serializer.
    public List<TransportOption> Options { get; set; } = [];
}

/// <summary>One key/value pair in a <see cref="TransportHint"/>.</summary>
public sealed class TransportOption
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}

/// <summary>The inbound half of the declarative schema: either a reference to a binary Kaitai Struct layout, or a list of text response patterns.</summary>
public sealed class InboundProtocol
{
    /// <summary>Path to a Kaitai Struct (.ksy) file, relative to the manifest's own location. Referenced only — not parsed by dev-term itself (see docs/design/device-manifests.md).</summary>
    public string? KaitaiFile { get; set; }

    public List<ResponsePattern> Patterns { get; set; } = [];
}

/// <summary>One recognizable response shape for a text protocol (a literal or regex match against what comes back).</summary>
public sealed class ResponsePattern
{
    public required string Name { get; set; }

    public required string Match { get; set; }
}

/// <summary>One outbound command: named parameters templated into a send template (e.g. "SOUR:VOLT {value}\r").</summary>
public sealed class OutboundCommand
{
    public required string Name { get; set; }

    public List<CommandParameter> Parameters { get; set; } = [];

    public required string Template { get; set; }
}

/// <summary>One parameter of an <see cref="OutboundCommand"/>.</summary>
public sealed class CommandParameter
{
    public required string Name { get; set; }

    public string Type { get; set; } = "string";

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public string? Unit { get; set; }
}
