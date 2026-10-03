using DevTerm.Transports.Tcp;

namespace DevTerm.Configuration;

/// <summary>
/// Runs a one-off LXI discovery scan for a front end's device picker (see <see cref="LxiDiscovery"/>). A scan listens for replies
/// for a couple of seconds, so callers on a UI thread should run it on a background thread, like <see cref="BleDeviceScanner"/>.
/// </summary>
public static class LxiDeviceScanner
{
    public static IReadOnlyList<LxiDeviceOption> Scan() =>
        [.. LxiDiscovery.ScanAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)).GetAwaiter().GetResult().Select(LxiDeviceOption.FromDevice)];
}
