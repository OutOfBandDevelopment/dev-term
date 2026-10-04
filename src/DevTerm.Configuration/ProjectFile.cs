using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration;

/// <summary>One connection in a <see cref="ProjectFile"/>: a name plus the same connection-relevant JSON a saved profile holds (transport settings and presenter choices).</summary>
public sealed record ProjectConnection(string Name, string ProfileJson)
{
    /// <summary>Binds <see cref="ProfileJson"/> onto a fresh <see cref="CliOptions"/> through the same path every profile uses.</summary>
    public CliOptions ToOptions()
    {
        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(ProfileJson))).Build();
        var options = new CliOptions();
        DevTermConfiguration.Bind(configuration, options);
        return options;
    }
}

/// <summary>
/// A project: a named set of connections, saved as one JSON file and reopened only on request
/// (<c>--project &lt;file&gt;</c>), never automatically. Holds connection state only (profiles and their
/// presenter choices); see docs/design/proposals/project-state.md.
/// </summary>
public sealed class ProjectFile
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    public string Name { get; set; } = string.Empty;

    public List<ProjectConnection> Connections { get; } = [];

    /// <summary>Builds a project from named connections, each captured with <see cref="DevTermConfiguration.ToProfileJson"/>.</summary>
    public static ProjectFile From(string name, IEnumerable<(string Name, CliOptions Options)> connections)
    {
        var project = new ProjectFile { Name = name };
        foreach (var (connectionName, options) in connections)
        {
            project.Connections.Add(new ProjectConnection(connectionName, DevTermConfiguration.ToProfileJson(options)));
        }

        return project;
    }

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["Name"] = Name,
            ["Connections"] = new JsonArray([.. Connections.Select(c => (JsonNode?)new JsonObject
            {
                ["Name"] = c.Name,
                ["Profile"] = JsonNode.Parse(c.ProfileJson),
            })]),
        };
        return root.ToJsonString(_indented);
    }

    /// <exception cref="InvalidDataException">The text is not a project file.</exception>
    public static ProjectFile FromJson(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("A project file must be a JSON object.");
            var project = new ProjectFile { Name = (string?)root["Name"] ?? string.Empty };
            foreach (var node in root["Connections"] as JsonArray ?? [])
            {
                if (node is not JsonObject connection || connection["Profile"] is not JsonObject profile)
                {
                    throw new InvalidDataException("Every connection needs a Profile object.");
                }

                project.Connections.Add(new ProjectConnection((string?)connection["Name"] ?? string.Empty, profile.ToJsonString()));
            }

            return project;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The project file is not valid JSON: " + ex.Message, ex);
        }
    }

    public void Save(string path) => AtomicFile.WriteAllText(path, ToJson());

    public static ProjectFile Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>The connection called <paramref name="name"/>, or the first one when <paramref name="name"/> is null or empty.</summary>
    public ProjectConnection? Find(string? name) =>
        string.IsNullOrEmpty(name)
            ? Connections.FirstOrDefault()
            : Connections.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
