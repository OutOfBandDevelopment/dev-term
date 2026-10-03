using System.Text.RegularExpressions;

namespace DevTerm.Core.Routing;

/// <summary>Which way a <see cref="RoutingRule"/> maps a message.</summary>
public enum RoutingDirection
{
    /// <summary>A line the device sent matches <see cref="RoutingRule.Match"/>; its payload is published to <see cref="RoutingRule.Topic"/>.</summary>
    DeviceToBroker,

    /// <summary>A broker message on <see cref="RoutingRule.Topic"/> matches <see cref="RoutingRule.Match"/>; <see cref="RoutingRule.Send"/> goes to the device.</summary>
    BrokerToDevice,
}

/// <summary>
/// One mapping rule, serializable as JSON (see <see cref="RoutingRuleSet"/>). <see cref="Match"/> is a regex
/// over the device line (device to broker) or the broker payload text (broker to device); its named groups
/// are available as <c>${name}</c> in <see cref="Payload"/>, <see cref="Topic"/> and <see cref="Send"/>.
/// See docs/design/proposals/message-broker-protocols.md.
/// </summary>
public sealed class RoutingRule
{
    private Regex? _compiled;

    public RoutingDirection Direction { get; set; }

    /// <summary>Device to broker: the topic to publish to. Broker to device: the topic that triggers the rule (exact match, or a trailing <c>#</c> wildcard).</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>A regex over the message text. Empty matches everything.</summary>
    public string Match { get; set; } = string.Empty;

    /// <summary>Device to broker only: the published payload. Defaults to the whole line.</summary>
    public string? Payload { get; set; }

    /// <summary>Broker to device only: the text sent to the device (the router appends the line terminator).</summary>
    public string? Send { get; set; }

    internal Regex Compiled => _compiled ??= new Regex(Match, RegexOptions.CultureInvariant);

    internal bool TopicMatches(string topic) =>
        Topic.EndsWith('#') ? topic.StartsWith(Topic[..^1], StringComparison.Ordinal) : string.Equals(Topic, topic, StringComparison.Ordinal);

    internal static string Expand(string template, Match match) =>
        Regex.Replace(template, @"\$\{(\w+)\}", m => match.Groups[m.Groups[1].Value] is { Success: true } g ? g.Value : string.Empty);
}
