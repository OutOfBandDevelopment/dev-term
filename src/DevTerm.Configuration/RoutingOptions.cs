using DevTerm.Core.Routing;

namespace DevTerm.Configuration;

/// <summary>
/// The routing proxy's section of a connection profile (<c>"Routing": { ... }</c>): which broker to talk to and the rules that
/// map device lines to broker topics and back. Routing starts by itself when a profile with a host and at least one rule
/// connects. The password is plain text in the profile (a development tool) and <c>DEVTERM_ROUTING__PASSWORD</c> overrides it.
/// See docs/specs/routing-window.md.
/// </summary>
public sealed class RoutingOptions
{
    /// <summary>The broker protocol: <c>mqtt</c>, <c>amqp</c> or <c>stomp</c>.</summary>
    public string Protocol { get; set; } = "mqtt";

    public string Host { get; set; } = string.Empty;

    /// <summary>The broker port; 0 uses the protocol's default (1883 MQTT, 5672 AMQP, 61613 STOMP).</summary>
    public int Port { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool Tls { get; set; }

    public List<RoutingRule> Rules { get; set; } = [];

    /// <summary>True when there is something to start: a host and at least one rule.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && Rules.Count > 0;

    public int EffectivePort => Port != 0 ? Port : Protocol.ToLowerInvariant() switch
    {
        "amqp" => 5672,
        "stomp" => 61613,
        _ => 1883,
    };

    /// <summary>The first problem that stops routing from starting, or null when it can start.</summary>
    public string? Validate()
    {
        if (Protocol.ToLowerInvariant() is not ("mqtt" or "amqp" or "stomp"))
        {
            return $"Unknown routing protocol '{Protocol}' (use mqtt, amqp or stomp).";
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            return "Routing needs a broker host.";
        }

        if (Port is < 0 or > 65535)
        {
            return "The routing port must be 1-65535.";
        }

        for (var i = 0; i < Rules.Count; i++)
        {
            if (Rules[i].Validate() is { } problem)
            {
                return $"Rule {i + 1}: {problem}";
            }
        }

        return null;
    }
}
