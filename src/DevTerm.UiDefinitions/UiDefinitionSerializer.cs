using System.Text.Json;
using System.Xml.Serialization;

namespace DevTerm.UiDefinitions;

/// <summary>JSON/XML round-trip for <see cref="UiDefinition"/>, via the framework's own polymorphic serialization support (no hand-rolled parsing).</summary>
public static class UiDefinitionSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly XmlSerializer XmlSerializerInstance = new(typeof(UiDefinition));

    public static string ToJson(UiDefinition definition) =>
        JsonSerializer.Serialize(definition, JsonOptions);

    public static UiDefinition FromJson(string json) =>
        JsonSerializer.Deserialize<UiDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException("Deserialized UI definition was null.");

    public static string ToXml(UiDefinition definition)
    {
        using var writer = new StringWriter();
        XmlSerializerInstance.Serialize(writer, definition);
        return writer.ToString();
    }

    public static UiDefinition FromXml(string xml)
    {
        using var reader = new StringReader(xml);
        return (UiDefinition?)XmlSerializerInstance.Deserialize(reader)
            ?? throw new InvalidOperationException("Deserialized UI definition was null.");
    }
}
