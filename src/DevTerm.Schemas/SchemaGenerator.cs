using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using DevTerm.Configuration;
using DevTerm.DeviceManifests;
using DevTerm.UiDefinitions;

namespace DevTerm.Schemas;

/// <summary>Exports a JSON Schema per hand-written format from the C# model itself, so a schema cannot drift from the code.</summary>
public static class SchemaGenerator
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    /// <summary>File name (e.g. <c>device-manifest.schema.json</c>) to schema text.</summary>
    public static IReadOnlyDictionary<string, string> Generate() => new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["device-manifest.schema.json"] = For<DeviceManifest>(),
        ["ui-definition.schema.json"] = For<UiDefinition>(),
        ["connection-profile.schema.json"] = For<CliOptions>(omit: [nameof(CliOptions.Password)]),
    };

    /// <param name="omit">Properties the type accepts but a saved file must never contain (a profile never stores a password).</param>
    private static string For<T>(string[]? omit = null)
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(_options, typeof(T));
        if (schema is JsonObject obj)
        {
            obj["$schema"] = "https://json-schema.org/draft/2020-12/schema";
            foreach (var name in omit ?? [])
            {
                (obj["properties"] as JsonObject)?.Remove(name);
            }
        }

        return schema.ToJsonString(_options).ReplaceLineEndings("\n") + "\n";
    }
}
