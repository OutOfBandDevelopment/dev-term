using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevTerm.Core.Routing;

/// <summary>A list of <see cref="RoutingRule"/>s, loaded from JSON.</summary>
public sealed class RoutingRuleSet
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public List<RoutingRule> Rules { get; set; } = [];

    public static RoutingRuleSet FromJson(string json) =>
        JsonSerializer.Deserialize<RoutingRuleSet>(json, _json) ?? new RoutingRuleSet();

    public string ToJson() => JsonSerializer.Serialize(this, _json);
}
