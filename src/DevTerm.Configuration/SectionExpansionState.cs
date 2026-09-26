using System.Collections.Concurrent;

namespace DevTerm.Configuration;

/// <summary>
/// Whether each control-panel section was last left expanded or collapsed, keyed by the panel's
/// definition name plus the section's label — so closing and reopening a panel (or opening the same
/// SCPI profile's panel again) restores the sections the user collapsed, in both the TUI's
/// <c>ControlPanelMode</c> and WPF's <c>ControlPanelWindow</c>. Kept for the life of the process
/// (the same in-process pattern as <see cref="LastPickedColors"/>); not persisted across restarts.
/// </summary>
public static class SectionExpansionState
{
    private static readonly ConcurrentDictionary<(string Definition, string Section), bool> _expanded = new();

    /// <summary>The remembered state, or expanded (true) for a section never toggled.</summary>
    public static bool IsExpanded(string definitionName, string sectionLabel) =>
        !_expanded.TryGetValue(Key(definitionName, sectionLabel), out var expanded) || expanded;

    public static void Set(string definitionName, string sectionLabel, bool expanded) =>
        _expanded[Key(definitionName, sectionLabel)] = expanded;

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
