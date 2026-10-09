using System.Collections.Concurrent;
using DevTerm.Configuration;
using DevTerm.Core.Sessions;

namespace DevTerm.Web;

/// <summary>
/// The extra connections the host opens from its <c>--project</c> file, each its own <see cref="SessionHub"/>.
/// Shared by the REST endpoints and the Blazor <c>/connections</c> page so both see the same set.
/// </summary>
/// <summary>An opened project connection; <see cref="ControlPort"/> and <see cref="ControlToken"/> are set only when its profile asked for <c>ControlHttp</c>.</summary>
public sealed record OpenedConnection(string Id, string Name, int? ControlPort = null, string? ControlToken = null);

public sealed class ConnectionManager
{
    private readonly string? _projectPath;
    private readonly ConnectionProfileStore? _store;
    private readonly int _backlogLines;
    private readonly ConcurrentDictionary<string, (string Name, SessionHub Hub, SessionHttpControlServer? Control, IDisposable? Registration)> _open = new();
    private readonly object _projectLock = new();

    /// <param name="store">The saved-profile store the TUI and WPF use; the connection set when there is no <paramref name="projectPath"/>.</param>
    public ConnectionManager(string? projectPath, int backlogLines, HostEvents events, ConnectionProfileStore? store = null)
    {
        _projectPath = projectPath;
        _store = store;
        _backlogLines = backlogLines;
        Events = events;
    }

    /// <summary>Raised after a connection opens (its id and hub), so a host-wide tool such as the Stream Monitor can follow it.</summary>
    public event Action<string, SessionHub>? ConnectionOpened;

    /// <summary>Raised after a connection closes (its id).</summary>
    public event Action<string>? ConnectionClosed;

    /// <summary>The extra connections open now (id and hub).</summary>
    public IReadOnlyList<(string Id, SessionHub Hub)> OpenHubs() => [.. _open.Select(c => (c.Key, c.Value.Hub))];

    /// <summary>The host's event stream; a page watches it to re-read <see cref="Open"/> when a connection opens or closes.</summary>
    public HostEvents Events { get; }

    /// <summary>Name and description of each project connection (no credentials), or empty without a readable project.</summary>
    public IReadOnlyList<(string Name, string Description)> Project()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_projectPath))
            {
                return _store is null ? [] : [.. _store.List().Select(n => (n, StoreDescription(n)))];
            }

            return [.. ProjectFile.Load(_projectPath).Connections.Select(c => (c.Name, ConnectionDescription.Definition(c.ToOptions())))];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The named project connection's settings (case-insensitive), or <see langword="null"/> when there is no such connection or no readable project.</summary>
    public CliOptions? ProjectOptions(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_projectPath))
            {
                return _store is not null && _store.List().Contains(name, StringComparer.OrdinalIgnoreCase) ? _store.Load(name) : null;
            }

            return !File.Exists(_projectPath) ? null : ProjectFile.Load(_projectPath).Find(name)?.ToOptions();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>True when the host was started with a <c>--project</c> file that edits can be saved to.</summary>
    public bool HasProject => !string.IsNullOrWhiteSpace(_projectPath) || _store is not null;

    private string StoreDescription(string name)
    {
        try
        {
            return ConnectionDescription.Definition(_store!.Load(name));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            return "(unreadable profile)";
        }
    }

    /// <summary>
    /// Adds or replaces the named connection in the project file from <paramref name="profileJson"/> (the JSON a saved
    /// profile holds, e.g. <c>{"Transport":"tcp","Host":"10.0.0.5","Port":23}</c>), creating the file when it does not exist yet.
    /// Returns an error message, or <see langword="null"/> when saved.
    /// </summary>
    public string? Upsert(string name, string profileJson)
    {
        if (!HasProject)
        {
            return "The host has no project file or profile store to save to.";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "A connection needs a name.";
        }

        ProjectConnection candidate;
        try
        {
            candidate = new ProjectConnection(name, profileJson);
            var validation = new CliOptionsValidator().Validate(null, candidate.ToOptions());
            if (validation.Failed)
            {
                return string.Join(" ", validation.Failures);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException or InvalidDataException)
        {
            return "The profile is not valid: " + ex.Message;
        }

        if (string.IsNullOrWhiteSpace(_projectPath))
        {
            try
            {
                _store!.Save(name.Trim(), candidate.ToOptions());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return "The profile could not be saved: " + ex.Message;
            }

            Events.Publish("project-changed", new { name });
            return null;
        }

        lock (_projectLock)
        {
            ProjectFile project;
            try
            {
                project = LoadOrNew();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or IOException)
            {
                return "The project file could not be read: " + ex.Message;
            }

            var index = project.Connections.FindIndex(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                project.Connections[index] = candidate with { History = project.Connections[index].History, Log = project.Connections[index].Log };
            }
            else
            {
                project.Connections.Add(candidate);
            }

            project.Save(_projectPath!);
        }

        Events.Publish("project-changed", new { name });
        return null;
    }

    /// <summary>Removes the named connection from the project file; <see langword="false"/> when there is no such connection.</summary>
    public bool Remove(string name)
    {
        if (!HasProject)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_projectPath))
        {
            try
            {
                if (!_store!.Delete(name))
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }

            Events.Publish("project-changed", new { name });
            return true;
        }

        lock (_projectLock)
        {
            ProjectFile project;
            try
            {
                project = LoadOrNew();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or IOException)
            {
                return false;
            }

            if (project.Connections.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                return false;
            }

            project.Save(_projectPath!);
        }

        Events.Publish("project-changed", new { name });
        return true;
    }

    private ProjectFile LoadOrNew() => File.Exists(_projectPath!) ? ProjectFile.Load(_projectPath!) : new ProjectFile { Name = Path.GetFileNameWithoutExtension(_projectPath!) };

    /// <summary>
    /// The connections as a project file (File > Save Project in the desktop apps): the extra connections open now, with whether each was
    /// logging, or the saved project's connections when none are open. Includes credentials, so callers must not offer it to a read-only viewer.
    /// Null when there is nothing to save.
    /// </summary>
    public string? ExportProject(string name)
    {
        var tabs = _open.Values.Select(c => new ProjectTabState(c.Name, c.Hub.Options, [], c.Hub.LogPath is not null)).ToList();
        if (tabs.Count == 0)
        {
            foreach (var (connectionName, _) in Project())
            {
                if (ProjectOptions(connectionName) is { } options)
                {
                    tabs.Add(new ProjectTabState(connectionName, options, [], false));
                }
            }
        }

        return tabs.Count == 0 ? null : ProjectFile.FromTabs(name, tabs, null).ToJson();
    }

    /// <summary>
    /// Adds every connection of a project file (File > Open Project) to this host's connections, replacing same-named ones; the file's
    /// window layout and per-tab history have no meaning for a web host and are ignored. Returns the names imported and an error per
    /// connection (or one for an unreadable file) that was not.
    /// </summary>
    public (IReadOnlyList<string> Imported, IReadOnlyList<string> Errors) ImportProject(string json)
    {
        ProjectFile project;
        try
        {
            project = ProjectFile.FromJson(json);
        }
        catch (InvalidDataException ex)
        {
            return ([], [ex.Message]);
        }

        var imported = new List<string>();
        var errors = new List<string>();
        foreach (var connection in project.Connections)
        {
            var error = Upsert(connection.Name, connection.ProfileJson);
            if (error is null)
            {
                imported.Add(connection.Name);
            }
            else
            {
                errors.Add($"{connection.Name}: {error}");
            }
        }

        return (imported, errors);
    }

    public IReadOnlyList<(string Id, string Name, string State)> Open() =>
        [.. _open.Select(c => (c.Key, c.Value.Name, c.Value.Hub.State.ToString()))];

    public bool TryGet(string id, out SessionHub hub)
    {
        var found = _open.TryGetValue(id, out var entry);
        hub = entry.Hub;
        return found;
    }

    /// <summary>
    /// Opens the named project connection; <see langword="null"/> when the project has no such connection. A profile with
    /// <c>ControlHttp</c> set also gets its own loopback control server on that port (one port per connection, token from
    /// <c>ControlToken</c> or random), closed with the connection.
    /// </summary>
    public async Task<OpenedConnection?> OpenAsync(string name, CancellationToken cancellationToken = default)
    {
        CliOptions? options = ProjectOptions(name);
        if (options is null)
        {
            return null;
        }

        var chosen = new { Name = name };
        var built = DevTermSessionBuilder.Build(options);
        var connection = new SessionHub(built.Session, built.Catalog, options, _backlogLines);
        SessionHttpControlServer? control = null;
        IDisposable? registration = null;
        try
        {
            if (options.ControlHttp > 0)
            {
                control = new SessionHttpControlServer(connection.Session, options.ControlHttp, text => TypedInput.TryEncode(connection.Catalog, options, text), options.ControlToken);
                registration = connection.Session.AddObserver(control);
            }

            await connection.StartAsync(cancellationToken);
        }
        catch
        {
            registration?.Dispose();
            if (control is not null)
            {
                await control.DisposeAsync();
            }

            await connection.DisposeAsync();
            throw;
        }

        var id = Guid.NewGuid().ToString("N")[..8];
        _open[id] = (chosen.Name, connection, control, registration);
        connection.LineReceived += line => Events.Publish("line", new { id, text = line });
        ConnectionOpened?.Invoke(id, connection);
        Events.Publish("connection-opened", new { id, name = chosen.Name });
        return new OpenedConnection(id, chosen.Name, control?.Port, control?.Token);
    }

    public async Task<bool> CloseAsync(string id)
    {
        if (!_open.TryRemove(id, out var removed))
        {
            return false;
        }

        removed.Registration?.Dispose();
        if (removed.Control is not null)
        {
            await removed.Control.DisposeAsync();
        }

        ConnectionClosed?.Invoke(id);
        await removed.Hub.DisposeAsync();
        Events.Publish("connection-closed", new { id });
        return true;
    }

    public async Task CloseAllAsync()
    {
        foreach (var id in _open.Keys.ToArray())
        {
            await CloseAsync(id);
        }
    }
}
