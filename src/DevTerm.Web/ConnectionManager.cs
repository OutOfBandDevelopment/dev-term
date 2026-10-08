using System.Collections.Concurrent;
using DevTerm.Configuration;

namespace DevTerm.Web;

/// <summary>
/// The extra connections the host opens from its <c>--project</c> file, each its own <see cref="SessionHub"/>.
/// Shared by the REST endpoints and the Blazor <c>/connections</c> page so both see the same set.
/// </summary>
public sealed class ConnectionManager
{
    private readonly string? _projectPath;
    private readonly int _backlogLines;
    private readonly ConcurrentDictionary<string, (string Name, SessionHub Hub)> _open = new();

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

    public IReadOnlyList<(string Id, string Name, string State)> Open() =>
        [.. _open.Select(c => (c.Key, c.Value.Name, c.Value.Hub.State.ToString()))];

    public bool TryGet(string id, out SessionHub hub)
    {
        var found = _open.TryGetValue(id, out var entry);
        hub = entry.Hub;
        return found;
    }

    /// <summary>Opens the named project connection; <see langword="null"/> when the project has no such connection.</summary>
    public async Task<(string Id, string Name)?> OpenAsync(string name, CancellationToken cancellationToken = default)
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
        await connection.StartAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("N")[..8];
        _open[id] = (chosen.Name, connection);
        connection.LineReceived += line => Events.Publish("line", new { id, text = line });
        Events.Publish("connection-opened", new { id, name = chosen.Name });
        return (id, chosen.Name);
    }

    public async Task<bool> CloseAsync(string id)
    {
        if (!_open.TryRemove(id, out var removed))
        {
            return false;
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
