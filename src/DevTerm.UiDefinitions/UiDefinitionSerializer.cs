using System.Text.Json;
using System.Xml.Serialization;

namespace DevTerm.UiDefinitions;

/// <summary>JSON/XML round-trip for <see cref="UiDefinition"/>, via the framework's own polymorphic serialization support (no hand-rolled parsing).</summary>
public static class UiDefinitionSerializer
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static readonly XmlSerializer _xmlSerializerInstance = new(typeof(UiDefinition));

    public static string ToJson(UiDefinition definition) =>
        JsonSerializer.Serialize(definition, _jsonOptions);

    public static UiDefinition FromJson(string json) =>
        JsonSerializer.Deserialize<UiDefinition>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Deserialized UI definition was null.");

    public static string ToXml(UiDefinition definition)
    {
        using var writer = new StringWriter();
        _xmlSerializerInstance.Serialize(writer, definition);
        return writer.ToString();
    }

    public static UiDefinition FromXml(string xml)
    {
        using var reader = new StringReader(xml);
        return (UiDefinition?)_xmlSerializerInstance.Deserialize(reader)
            ?? throw new InvalidOperationException("Deserialized UI definition was null.");
    }
}
