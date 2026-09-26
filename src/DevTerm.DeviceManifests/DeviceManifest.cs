using System.Text.Json.Serialization;
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

    /// <summary>
    /// Appended to every outbound command's formatted template before it's sent (e.g. <c>"\n"</c>);
    /// empty by default, for a manifest whose templates already carry their own terminator.
    /// </summary>
    public string Terminator { get; set; } = string.Empty;

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

    /// <summary>
    /// Whether replies end in CR, LF, or CRLF (true, the default). False for a device whose replies
    /// have no terminator at all: whatever arrives in one read then counts as one complete reply
    /// (see <c>DevTerm.Core.Presenters.LineReplyPresenter.ConfigureTerminator</c>).
    /// </summary>
    public bool LineTerminated { get; set; } = true;
}

/// <summary>
/// One recognizable response shape for a text protocol: a regex tested against every complete
/// inbound line (a correlated reply or an unsolicited one alike). On a match, the line publishes
/// live values a panel's indicators and charts read (<c>IStructuredPresenter.ValuesChanged</c>): the
/// pattern's <see cref="Name"/> gets the first capture group (the whole match when there's none),
/// and every named group (<c>(?&lt;chA&gt;...)</c>) gets its own value under its group name.
/// </summary>
public sealed class ResponsePattern
{
    public required string Name { get; set; }

    public required string Match { get; set; }
}

/// <summary>
/// One outbound command: named parameters templated into a send template (e.g. "SOUR:VOLT {value}\r").
/// A control invokes it by <see cref="EffectiveId"/> (a <c>UiControl.Id</c>, or a button's
/// <c>CommandId</c>); its value arrives comma-joined when a button reads several parameter fields.
/// </summary>
public sealed class OutboundCommand
{
    public required string Name { get; set; }

    /// <summary>The command id a control invokes; <see cref="Name"/> when unset.</summary>
    public string? Id { get; set; }

    public List<CommandParameter> Parameters { get; set; } = [];

    public required string Template { get; set; }

    /// <summary>Whether the next reply line belongs to this command — shown by the indicator <see cref="EffectiveReplyId"/>.</summary>
    public bool IsQuery { get; set; }

    /// <summary>The indicator id a query's reply publishes to; <c>{EffectiveId}.reply</c> when unset (the SCPI module's convention).</summary>
    public string? ReplyId { get; set; }

    /// <summary><see cref="Id"/>, else <see cref="Name"/>.</summary>
    [JsonIgnore]
    public string EffectiveId => string.IsNullOrWhiteSpace(Id) ? Name : Id;

    /// <summary>The reply indicator id for a query (<see cref="IsQuery"/> or an explicit <see cref="ReplyId"/>); null otherwise.</summary>
    [JsonIgnore]
    public string? EffectiveReplyId => ReplyId ?? (IsQuery ? $"{EffectiveId}.reply" : null);
}

/// <summary>One parameter of an <see cref="OutboundCommand"/>, substituted for <c>{Name}</c> in its template.</summary>
public sealed class CommandParameter
{
    public required string Name { get; set; }

    /// <summary><c>string</c> (sent as typed), or <c>number</c>/<c>integer</c> (parsed, clamped to [Minimum, Maximum], formatted).</summary>
    public string Type { get; set; } = "string";

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public string? Unit { get; set; }

    /// <summary>Used when no value arrives for this parameter (or a number doesn't parse).</summary>
    public string? DefaultValue { get; set; }

    /// <summary>A .NET numeric format string for a number (e.g. <c>"00.00"</c> for a Korad-style <c>VSET1:05.00</c>); invariant-culture shortest form when unset.</summary>
    public string? Format { get; set; }

    /// <summary>Whether <see cref="Type"/> is a numeric kind.</summary>
    [JsonIgnore]
    public bool IsNumeric =>
        Type.Equals("number", StringComparison.OrdinalIgnoreCase)
        || Type.Equals("numeric", StringComparison.OrdinalIgnoreCase)
        || Type.Equals("integer", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsInteger => Type.Equals("integer", StringComparison.OrdinalIgnoreCase);
}
