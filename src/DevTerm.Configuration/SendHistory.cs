using System.Collections.ObjectModel;

namespace DevTerm.Configuration;

/// <summary>
/// Bounded, most-recent-first history of lines a user has typed into a front end's send field —
/// shared by the TUI's <c>TuiMode</c> (a <c>TextField</c> with manual Up/Down handling, since
/// Terminal.Gui 2.5.0 has no combo box) and WPF's <c>MainWindow</c> (an editable <c>ComboBox</c>) so
/// Up/Down cycles through prior input the same way a shell history does. In-memory only for now, for
/// the life of the process — not persisted across restarts.
/// </summary>
public sealed class SendHistory
{
    public const int Capacity = 100;

    private readonly ObservableCollection<string> _items = [];
    private int _cursor = -1;

    /// <summary>Every recorded line, most recent first. A WPF <c>ComboBox</c> can bind its <c>ItemsSource</c> directly to this for a live-updating drop-down.</summary>
    public ObservableCollection<string> Items => _items;

    /// <summary>
    /// Records a newly sent line and resets Up/Down navigation back to "not currently recalling
    /// anything." Blank lines are not recorded.
    /// </summary>
    public void Add(string line)
    {
        _cursor = -1;
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        _items.Insert(0, line);
        while (_items.Count > Capacity)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }

    /// <summary>Moves toward older entries (Up arrow). Returns null once already at the oldest entry, or if there's no history.</summary>
    public string? Previous()
    {
        if (_cursor + 1 >= _items.Count)
        {
            return null;
        }

        _cursor++;
        return _items[_cursor];
    }

    /// <summary>Moves toward newer entries (Down arrow). Returns "" once back past the newest entry (nothing selected); null if already there.</summary>
    public string? Next()
    {
        if (_cursor < 0)
        {
            return null;
        }

        _cursor--;
        return _cursor >= 0 ? _items[_cursor] : string.Empty;
    }

    /// <summary>Resets Up/Down navigation without touching recorded history — called whenever the field is edited by hand.</summary>
    public void ResetCursor() => _cursor = -1;
}
