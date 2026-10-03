using System.ComponentModel.DataAnnotations;

namespace DevTerm.Transports.Mqtt;

/// <summary>
/// Configuration for an MQTT session: one connection to a broker, any number of subscribed topic
/// filters, and one default publish topic. See docs/design/proposals/message-broker-protocols.md.
/// </summary>
public sealed class MqttTransportOptions
{
    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 1883;

    /// <summary>Blank means a generated, unique client id.</summary>
    public string? ClientId { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Topic filters to subscribe to on open (wildcards allowed). Empty means publish-only.</summary>
    public List<string> SubscribeTopics { get; set; } = [];

    /// <summary>Where a sent line is published unless it names its own topic (<c>topic&lt;TAB&gt;payload</c>).</summary>
    public string? PublishTopic { get; set; }

    [Range(0, 2)]
    public int QualityOfService { get; set; }

    /// <summary>Milliseconds a connect, subscribe or publish may take before it fails.</summary>
    [Range(1, int.MaxValue)]
    public int TimeoutMs { get; set; } = 5000;
}
