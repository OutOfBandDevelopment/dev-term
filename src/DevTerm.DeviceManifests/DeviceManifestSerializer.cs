using System.Text.Json;
using System.Xml.Serialization;

namespace DevTerm.DeviceManifests;

/// <summary>JSON/XML round-trip for <see cref="DeviceManifest"/>, mirroring <c>DevTerm.UiDefinitions.UiDefinitionSerializer</c>.</summary>
public static class DeviceManifestSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly XmlSerializer XmlSerializerInstance = new(typeof(DeviceManifest));

    public static string ToJson(DeviceManifest manifest) =>
        JsonSerializer.Serialize(manifest, JsonOptions);

    public static DeviceManifest FromJson(string json) =>
        JsonSerializer.Deserialize<DeviceManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Deserialized device manifest was null.");

    public static string ToXml(DeviceManifest manifest)
    {
        using var writer = new StringWriter();
        XmlSerializerInstance.Serialize(writer, manifest);
        return writer.ToString();
    }

    public static DeviceManifest FromXml(string xml)
    {
        using var reader = new StringReader(xml);
        return (DeviceManifest?)XmlSerializerInstance.Deserialize(reader)
            ?? throw new InvalidOperationException("Deserialized device manifest was null.");
    }
}
