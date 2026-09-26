using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Configuration;

/// <summary>
/// Loads the real, platform-specific BLE backend into DI at runtime, in place of
/// <c>DevTerm.Transports.Ble</c>'s default "unsupported platform" registrations. This exists
/// because a per-OS BLE backend (<c>Windows.Devices.Bluetooth</c> on Windows, later BlueZ on Linux,
/// CoreBluetooth on macOS - see docs/design/transports.md's "BLE" section) needs a Windows-versioned
/// TFM to compile against its native API, and neither front end that would offer BLE as a transport
/// (<c>DevTerm.Console</c>, plain <c>net10.0</c>) can take a compile-time <c>ProjectReference</c> on
/// a project whose only TFM is Windows-specific - the .NET SDK refuses that reference at restore
/// time as incompatible. Loading the assembly by reflection instead keeps every front end on its
/// current TFM and degrades gracefully (the "unsupported platform" registrations stay in effect)
/// when the backend assembly isn't present, e.g. on Linux/macOS or a trimmed publish that dropped it.
/// </summary>
public static class BlePlatformAdapterLoader
{
    private const string _windowsBackendAssemblyName = "DevTerm.Transports.Ble.Windows.dll";
    private const string _windowsBackendTypeName = "DevTerm.Transports.Ble.Windows.ServiceCollectionExtensions";
    private const string _windowsBackendMethodName = "AddWindowsBleAdapter";

    /// <summary>
    /// Best-effort: registers the platform BLE backend if one is available for the current OS and
    /// found alongside the running app, otherwise leaves whatever <c>AddBleTransport</c> already
    /// registered untouched. Must be called after <c>AddBleTransport</c> so its real registrations
    /// (added via plain <c>AddSingleton</c>, not <c>TryAddSingleton</c>) take precedence when a
    /// service is resolved.
    /// </summary>
    public static void TryRegisterPlatformAdapter(IServiceCollection services)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, _windowsBackendAssemblyName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var assembly = Assembly.LoadFrom(path);
            var type = assembly.GetType(_windowsBackendTypeName, throwOnError: false);
            var method = type?.GetMethod(_windowsBackendMethodName, BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, [services]);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or ReflectionTypeLoadException or TargetInvocationException or MemberAccessException)
        {
            // Best-effort: BLE just isn't available this run (missing dependency, load failure, ...).
            // The default "unsupported platform" registrations from AddBleTransport stay in effect,
            // so selecting the BLE transport still fails - just with a clear message at connect time
            // instead of a crash here at startup.
        }
    }
}
