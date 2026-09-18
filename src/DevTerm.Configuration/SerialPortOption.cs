namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.SerialPortOptions"/> — a serial port that's
/// actually attached, with the <see cref="Name"/> that gets written into
/// <see cref="ConnectionEditorViewModel.Port"/> when picked (<c>"COM3"</c>) and a
/// <see cref="Display"/> string for the picker that adds the OS's description when one is known
/// (<c>"COM3 — Prolific USB-to-Serial Comm Port"</c>), falling back to just the name.
/// </summary>
public sealed record SerialPortOption(string Name, string Display)
{
    public static SerialPortOption From(string name, IReadOnlyDictionary<string, string> descriptions)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(descriptions);
        var display = descriptions.TryGetValue(name, out var description) && !string.IsNullOrWhiteSpace(description)
            ? $"{name} — {description.Trim()}"
            : name;
        return new SerialPortOption(name, display);
    }
}
