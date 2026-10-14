using System.Collections.Concurrent;
using System.Text.Json;

namespace DevTerm.Configuration;

/// <summary>
/// Whether each control-panel section was last left expanded or collapsed, keyed by the panel's
/// definition name plus the section's label — so closing and reopening a panel (or opening the same
/// SCPI profile's panel again) restores the sections the user collapsed, in both the TUI's
/// <c>ControlPanelMode</c> and WPF's <c>ControlPanelWindow</c>. Kept for the life of the process
/// (the same in-process pattern as <see cref="LastPickedColors"/>). A front end's startup calls
/// <see cref="UsePersistence"/> so the collapsed sections also survive a restart; tests don't, so they never touch the user's home.
/// </summary>
public static class SectionExpansionState
{
    private static readonly ConcurrentDictionary<(string Definition, string Section), bool> _expanded = new();
    private static readonly object _fileLock = new();
    private static string? _path;

    /// <summary>
    /// Loads the collapsed sections saved at <paramref name="path"/> and saves every later change there (best effort:
    /// an unreadable or unwritable file just means the state is not remembered). Only collapsed sections are stored,
    /// since expanded is the default.
    /// </summary>
    public static void UsePersistence(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_fileLock)
        {
            _path = path;
            try
            {
                if (File.Exists(path)
                    && JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path)) is { } saved)
                {
                    foreach (var (definition, sections) in saved)
                    {
                        foreach (var section in sections)
                        {
                            _expanded[(definition, section)] = false;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
    }

    /// <summary>Stops saving (tests that exercised <see cref="UsePersistence"/> call this).</summary>
    public static void StopPersistence()
    {
        lock (_fileLock)
        {
            _path = null;
        }
    }

    /// <summary>The remembered state, or expanded (true) for a section never toggled.</summary>
    public static bool IsExpanded(string definitionName, string sectionLabel) =>
        !_expanded.TryGetValue(Key(definitionName, sectionLabel), out var expanded) || expanded;

    public static void Set(string definitionName, string sectionLabel, bool expanded)
    {
        _expanded[Key(definitionName, sectionLabel)] = expanded;
        Save();
    }

    private static void Save()
    {
        lock (_fileLock)
        {
            if (_path is null)
            {
                return;
            }

            try
            {
                var collapsed = _expanded
                    .Where(pair => !pair.Value)
                    .GroupBy(pair => pair.Key.Definition)
                    .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key.Section).Order(StringComparer.Ordinal).ToList());
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                File.WriteAllText(_path, JsonSerializer.Serialize(collapsed, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Forgets every remembered state for <paramref name="definitionName"/> (the state is process-wide — tests use this to start from all-expanded).</summary>
    public static void Forget(string definitionName)
    {
        foreach (var key in _expanded.Keys.Where(k => k.Definition == definitionName))
        {
            _expanded.TryRemove(key, out _);
        }
    }

    /// <summary>Forgets everything remembered.</summary>
    public static void Clear() => _expanded.Clear();

    private static (string, string) Key(string definitionName, string sectionLabel)
    {
        ArgumentNullException.ThrowIfNull(definitionName);
        ArgumentNullException.ThrowIfNull(sectionLabel);
        return (definitionName, sectionLabel);
    }
}
