namespace DevTerm.Transports.Serial;

/// <summary>Enumerates available serial ports, e.g. for a "--list-ports" CLI action or a GUI port picker.</summary>
public interface ISerialPortDiscovery
{
    IReadOnlyList<string> GetPortNames();

    /// <summary>
    /// A human-readable description per port name (<c>"COM3"</c> → <c>"Prolific USB-to-Serial Comm Port"</c>),
    /// for a picker that wants to say more than the short name — best-effort and never required: a
    /// port with no entry here is still a perfectly good port, just shown by its short name alone.
    /// Keyed case-insensitively. The default is "no descriptions", which is also all a platform with
    /// no wired-up source (see <see cref="SystemSerialPortDiscovery"/>) reports.
    /// </summary>
    IReadOnlyDictionary<string, string> GetPortDescriptions() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class SystemSerialPortDiscovery : ISerialPortDiscovery
{
    public IReadOnlyList<string> GetPortNames() => System.IO.Ports.SerialPort.GetPortNames();

    /// <summary>
    /// Windows only for now: the Plug-and-Play registry keeps a friendly name per device, and reading
    /// it needs neither WMI nor a new package (see <see cref="WindowsSerialPortDescriptions"/>).
    /// Linux/macOS have no equivalent wired up yet, so they report no descriptions and the picker
    /// shows short names, exactly as before.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetPortDescriptions() =>
        OperatingSystem.IsWindows()
            ? WindowsSerialPortDescriptions.Read()
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Windows' friendly names embed the port — <c>"USB Serial Device (COM3)"</c> — which a picker
    /// already shows next to the description, so drop the trailing <c>(COMn)</c>. Returns the input
    /// (trimmed) if it doesn't end that way.
    /// </summary>
    public static string StripPortSuffix(string friendlyName, string portName)
    {
        ArgumentNullException.ThrowIfNull(friendlyName);
        ArgumentNullException.ThrowIfNull(portName);
        var trimmed = friendlyName.Trim();
        var suffix = $"({portName})";
        return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^suffix.Length].TrimEnd()
            : trimmed;
    }
}
