using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DevTerm.Transports.Serial;

/// <summary>
/// Reads Windows' friendly names for serial ports straight from the Plug-and-Play device registry
/// (<c>HKLM\SYSTEM\CurrentControlSet\Enum\{bus}\{device}\{instance}</c>): each device instance that
/// owns a port has a <c>Device Parameters\PortName</c> value (<c>"COM3"</c>) and, usually, a
/// <c>FriendlyName</c> (<c>"Prolific USB-to-Serial Comm Port (COM3)"</c>). Chosen over WMI's
/// <c>Win32_PnPEntity</c> because it needs no extra package and doesn't spin up the WMI service
/// (~a second on a cold start); it's read-only and needs no elevation.
///
/// The registry also remembers ports of devices that were unplugged long ago, so the result is a
/// lookup to apply to whatever <see cref="System.IO.Ports.SerialPort.GetPortNames"/> reports, never
/// a source of ports itself. Best-effort throughout: a key that can't be read is skipped, and a
/// failure to open the registry at all just yields no descriptions.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsSerialPortDescriptions
{
    private const string _enumKey = @"SYSTEM\CurrentControlSet\Enum";

    public static IReadOnlyDictionary<string, string> Read()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var enumRoot = Registry.LocalMachine.OpenSubKey(_enumKey);
            if (enumRoot is null)
            {
                return result;
            }

            foreach (var bus in enumRoot.GetSubKeyNames())
            {
                using var busKey = TryOpen(enumRoot, bus);
                if (busKey is null)
                {
                    continue;
                }

                foreach (var device in busKey.GetSubKeyNames())
                {
                    using var deviceKey = TryOpen(busKey, device);
                    if (deviceKey is null)
                    {
                        continue;
                    }

                    foreach (var instance in deviceKey.GetSubKeyNames())
                    {
                        using var instanceKey = TryOpen(deviceKey, instance);
                        Collect(instanceKey, result);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return result;
    }

    private static void Collect(RegistryKey? instanceKey, Dictionary<string, string> result)
    {
        if (instanceKey is null)
        {
            return;
        }

        using var parameters = TryOpen(instanceKey, "Device Parameters");
        if (parameters?.GetValue("PortName") is not string portName || portName.Length == 0)
        {
            return;
        }

        if (instanceKey.GetValue("FriendlyName") is not string friendlyName || friendlyName.Trim().Length == 0)
        {
            return;
        }

        var description = SystemSerialPortDiscovery.StripPortSuffix(friendlyName, portName);
        if (description.Length > 0)
        {
            // A port name can appear under more than one (stale) device instance; keep the first
            // rather than flip-flopping on registry enumeration order between runs of the same set.
            result.TryAdd(portName, description);
        }
    }

    private static RegistryKey? TryOpen(RegistryKey parent, string name)
    {
        try
        {
            return parent.OpenSubKey(name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
