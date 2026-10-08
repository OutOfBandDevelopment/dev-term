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
    private readonly int _backlogLines;
    private readonly ConcurrentDictionary<string, (string Name, SessionHub Hub, SessionHttpControlServer? Control, IDisposable? Registration)> _open = new();
    private readonly object _projectLock = new();

    public ConnectionManager(string? projectPath, int backlogLines, HostEvents events)
    {
        _projectPath = projectPath;
        _backlogLines = backlogLines;
        Events = events;
    }

    /// <summary>The host's event stream; a page watches it to re-read <see cref="Open"/> when a connection opens or closes.</summary>
    public HostEvents Events { get; }

    /// <summary>Name and description of each project connection (no credentials), or empty without a readable project.</summary>
    public IReadOnlyList<(string Name, string Description)> Project()
    {
        try
        {
            return string.IsNullOrWhiteSpace(_projectPath)
                ? []
                : [.. ProjectFile.Load(_projectPath).Connections.Select(c => (c.Name, ConnectionDescription.Definition(c.ToOptions())))];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>True when the host was started with a <c>--project</c> file that edits can be saved to.</summary>
    public bool HasProject => !string.IsNullOrWhiteSpace(_projectPath);

    /// <summary>
    /// Adds or replaces the named connection in the project file from <paramref name="profileJson"/> (the JSON a saved
    /// profile holds, e.g. <c>{"Transport":"tcp","Host":"10.0.0.5","Port":23}</c>), creating the file when it does not exist yet.
    /// Returns an error message, or <see langword="null"/> when saved.
    /// </summary>
    public string? Upsert(string name, string profileJson)
    {
        if (!HasProject)
        {
            return "The host was started without --project, so there is no file to save to.";
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
        ProjectConnection? chosen = null;
        try
        {
            chosen = string.IsNullOrWhiteSpace(_projectPath) ? null : ProjectFile.Load(_projectPath).Find(name);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }

        if (chosen is null)
        {
            return null;
        }

        var options = chosen.ToOptions();
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
