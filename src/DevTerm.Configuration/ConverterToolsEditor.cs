namespace DevTerm.Configuration;

/// <summary>
/// The working copy behind both front ends' "Converter tools..." dialog: a list of
/// <see cref="StreamConvertToolOptions"/> that can be added to, removed from and reordered (order matters,
/// since Auto picks the first tool whose formats match), plus the validation the dialog's OK button runs.
/// Edits go to copies, so cancelling leaves the profile's tools untouched.
/// See docs/design/features/stream-converter-tools.md.
/// </summary>
public sealed class ConverterToolsEditor
{
    private readonly List<StreamConvertToolOptions> _tools;

    public ConverterToolsEditor(IEnumerable<StreamConvertToolOptions> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = [.. tools.Select(StreamConvertToolOptions.Clone)];
    }

    /// <summary>The tools in their current order. Fields of the items may be edited in place.</summary>
    public IReadOnlyList<StreamConvertToolOptions> Tools => _tools;

    /// <summary>Appends a new tool with a name no other tool uses, and returns it.</summary>
    public StreamConvertToolOptions Add()
    {
        var number = _tools.Count + 1;
        string name;
        do
        {
            name = "tool" + number++;
        }
        while (_tools.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)));

        var tool = new StreamConvertToolOptions { Name = name, Arguments = "{input} {output}" };
        _tools.Add(tool);
        return tool;
    }

    /// <summary>Removes the tool at <paramref name="index"/>; returns the index to select afterwards (-1 when none are left).</summary>
    public int Remove(int index)
    {
        if (index < 0 || index >= _tools.Count)
        {
            return Math.Min(index, _tools.Count - 1);
        }

        _tools.RemoveAt(index);
        return Math.Min(index, _tools.Count - 1);
    }

    /// <summary>Moves the tool at <paramref name="index"/> one place earlier; returns its new index.</summary>
    public int MoveUp(int index) => Move(index, -1);

    /// <summary>Moves the tool at <paramref name="index"/> one place later; returns its new index.</summary>
    public int MoveDown(int index) => Move(index, 1);

    /// <summary>The first problem with the list (blank or repeated name, no path, non-positive DPI, blank output extension), or null when it is valid.</summary>
    public string? Validate()
    {
        for (var i = 0; i < _tools.Count; i++)
        {
            var tool = _tools[i];
            var label = string.IsNullOrWhiteSpace(tool.Name) ? $"Tool {i + 1}" : $"'{tool.Name}'";
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                return $"{label} needs a name.";
            }

            if (_tools.Take(i).Any(t => string.Equals(t.Name.Trim(), tool.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return $"Two tools are named '{tool.Name.Trim()}'; names must be unique.";
            }

            if (string.IsNullOrWhiteSpace(tool.Path))
            {
                return $"{label} needs the path to its executable.";
            }

            if (tool.Dpi <= 0)
            {
                return $"{label}: DPI must be greater than zero.";
            }

            if (string.IsNullOrWhiteSpace(tool.OutputExtension))
            {
                return $"{label} needs an output extension (for example png).";
            }
        }

        return null;
    }

    /// <summary>The edited list, trimmed, ready to store on a profile. Call <see cref="Validate"/> first.</summary>
    public List<StreamConvertToolOptions> ToList() =>
        [.. _tools.Select(t =>
        {
            var copy = StreamConvertToolOptions.Clone(t);
            copy.Name = copy.Name.Trim();
            copy.Path = copy.Path?.Trim();
            copy.Formats = copy.Formats.Trim();
            copy.OutputExtension = copy.OutputExtension.Trim().TrimStart('.');
            return copy;
        })];

    private int Move(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= _tools.Count || target < 0 || target >= _tools.Count)
        {
            return index;
        }

        (_tools[index], _tools[target]) = (_tools[target], _tools[index]);
        return target;
    }
}
