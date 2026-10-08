using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace DevTerm.Configuration;

/// <summary>The live state of one open tab, as captured into a project: its connection plus its send history and whether it was logging.</summary>
public sealed record ProjectTabState(string Name, CliOptions Options, IReadOnlyList<string> History, bool Logging);

/// <summary>The main window's position and size when a project was saved (device-independent pixels), and whether it was maximized. Only the WPF front end uses it.</summary>
public sealed record ProjectWindowBounds(double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>
/// One connection in a <see cref="ProjectFile"/>: a name plus the same connection-relevant JSON a saved profile holds
/// (transport settings and presenter choices), and optionally the tab's <see cref="History"/> (most recent first) and
/// <see cref="Log"/> setting (a <c>--log</c> value: <c>true</c> for an automatic file, or a path).
/// </summary>
public sealed record ProjectConnection(string Name, string ProfileJson, IReadOnlyList<string>? History = null, string? Log = null)
{
    /// <summary>Binds <see cref="ProfileJson"/> (and <see cref="Log"/>) onto a fresh <see cref="CliOptions"/> through the same path every profile uses.</summary>
    public CliOptions ToOptions()
    {
        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(ProfileJson))).Build();
        var options = new CliOptions();
        DevTermConfiguration.Bind(configuration, options);
        if (Log is { Length: > 0 })
        {
            options.Log = Log;
        }

        return options;
    }
}

/// <summary>
/// A project: a named set of connections, saved as one JSON file and reopened only on request
/// (<c>--project &lt;file&gt;</c>), never automatically. Holds connection state only (profiles and their
/// presenter choices) plus, optionally, each tab's send history and log setting and which tab was active; see docs/design/proposals/project-state.md.
/// </summary>
public sealed class ProjectFile
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    public string Name { get; set; } = string.Empty;

    public List<ProjectConnection> Connections { get; } = [];

    /// <summary>The name of the connection whose tab was in front when the project was saved (the window layout), or null.</summary>
    public string? Active { get; set; }

    /// <summary>The main window's bounds when saved (WPF), or null.</summary>
    public ProjectWindowBounds? Window { get; set; }

    /// <summary>Builds a project from open tabs, keeping each tab's send history and log setting and which one is <paramref name="active"/>.</summary>
    public static ProjectFile FromTabs(string name, IEnumerable<ProjectTabState> tabs, string? active)
    {
        var project = new ProjectFile { Name = name, Active = active };
        foreach (var tab in tabs)
        {
            var log = tab.Options.Log is { Length: > 0 } configured ? configured : tab.Logging ? SessionLogging.AutoPathValue : null;
            project.Connections.Add(new ProjectConnection(tab.Name, DevTermConfiguration.ToProfileJson(tab.Options), tab.History.Count > 0 ? [.. tab.History] : null, log));
        }

        return project;
    }

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
            ["Active"] = Active,
            ["Window"] = Window is { } w ? new JsonObject { ["Left"] = w.Left, ["Top"] = w.Top, ["Width"] = w.Width, ["Height"] = w.Height, ["Maximized"] = w.Maximized } : null,
            ["Connections"] = new JsonArray([.. Connections.Select(c => (JsonNode?)new JsonObject
            {
                ["Name"] = c.Name,
                ["Profile"] = JsonNode.Parse(c.ProfileJson),
                ["History"] = c.History is { Count: > 0 } ? new JsonArray([.. c.History.Select(h => (JsonNode?)JsonValue.Create(h))]) : null,
                ["Log"] = c.Log,
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
            var project = new ProjectFile { Name = (string?)root["Name"] ?? string.Empty, Active = (string?)root["Active"] };
            if (root["Window"] is JsonObject window
                && window["Left"] is JsonValue left && window["Top"] is JsonValue top
                && window["Width"] is JsonValue width && window["Height"] is JsonValue height
                && left.TryGetValue<double>(out var l) && top.TryGetValue<double>(out var t)
                && width.TryGetValue<double>(out var w) && height.TryGetValue<double>(out var h))
            {
                project.Window = new ProjectWindowBounds(l, t, w, h, window["Maximized"] is JsonValue m && m.TryGetValue<bool>(out var max) && max);
            }

            foreach (var node in root["Connections"] as JsonArray ?? [])
            {
                if (node is not JsonObject connection || connection["Profile"] is not JsonObject profile)
                {
                    throw new InvalidDataException("Every connection needs a Profile object.");
                }

                var history = (connection["History"] as JsonArray)?.Select(h => (string?)h).OfType<string>().ToList();
                project.Connections.Add(new ProjectConnection((string?)connection["Name"] ?? string.Empty, profile.ToJsonString(), history, (string?)connection["Log"]));
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
