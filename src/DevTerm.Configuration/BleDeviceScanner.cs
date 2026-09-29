using DevTerm.Transports.Ble;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Configuration;

/// <summary>
/// Runs a one-off BLE discovery scan for a front end's device picker — the same ad-hoc
/// <see cref="ServiceCollection"/> + <see cref="BlePlatformAdapterLoader"/> wiring
/// <c>Program.cs</c>'s <c>--listbledevices</c> action uses, factored out so <c>ConfigureMode</c>
/// (TUI) and <c>DeviceProfilesWindow</c> (WPF) don't each build their own container. Not something
/// <see cref="ConnectionEditorViewModel"/> itself can do: <see cref="IBleDeviceDiscovery"/>'s real
/// implementation only exists in a Windows-only assembly loaded at runtime (see
/// <see cref="BlePlatformAdapterLoader"/>), and a real scan takes several seconds — both rule out
/// running it eagerly from the view model's constructor the way <see cref="HidDeviceOption"/>'s fast,
/// cross-platform discovery does.
/// </summary>
public static class BleDeviceScanner
{
    /// <summary>Blocks for the scan's duration (a few seconds, see <c>WindowsBleDeviceDiscovery</c>) — callers on a UI thread should run this on a background thread.</summary>
    public static IReadOnlyList<BleDeviceOption> Scan()
    {
        var services = new ServiceCollection();
        services.AddBleTransport();
        BlePlatformAdapterLoader.TryRegisterPlatformAdapter(services);
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IBleDeviceDiscovery>().GetDevices().Select(BleDeviceOption.FromDescriptor)];
    }

    /// <summary>
    /// Enumerates one already-picked peripheral's GATT services/characteristics ("sub-device" UUIDs)
    /// for the service/characteristic pickers — same ad-hoc DI wiring as <see cref="Scan"/>, blocking
    /// for the enumeration's duration; callers on a UI thread should run this on a background thread.
    /// </summary>
    public static IReadOnlyList<BleGattServiceDescriptor> ExploreCharacteristics(string deviceId)
    {
        var services = new ServiceCollection();
        services.AddBleTransport();
        BlePlatformAdapterLoader.TryRegisterPlatformAdapter(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IBleGattProfileExplorer>().GetServices(deviceId);
    }
}
