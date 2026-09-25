using System.IO.Ports;
using System.Net.Sockets;
using HidSharp;

namespace DevTerm.Test.Utilities;

/// <summary>
/// Preflight checks for real-hardware <c>Integration</c> tests so a device left offline (bench
/// powered down, bridge unplugged, cable disconnected) degrades to <c>Assert.Inconclusive</c> the
/// same way a missing <c>.runsettings</c> parameter already does, rather than failing or hanging.
/// Shared by <c>DevTerm.Console.Tests.RealHardwareCliTests</c>/<c>RealHardwareSerialTests</c>,
/// <c>DevTerm.Wpf.Tests.RealHardwareMainWindowTests</c>, and the real-hardware HID tests in
/// <c>DevTerm.Devices.K8055.Tests</c>/<c>DevTerm.Devices.Busylight.Tests</c>.
/// </summary>
public static class RealDeviceReachability
{
    /// <summary>
    /// A plain TCP connect attempt is a truer reachability signal than ICMP ping for the
    /// serial-to-Ethernet bridges these real devices sit behind — they may not answer ICMP at all
    /// even when the TCP service itself is up — and it needs no elevated/raw-socket privilege.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    public static async Task<bool> IsTcpReachableAsync(string host, int port, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            await client.ConnectAsync(host, port, timeoutCts.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="portName"/> (e.g. "COM6") is currently enumerated by the OS. This
    /// only confirms the port exists — not that a device is actually attached/powered/answering at
    /// the far end, which the real-hardware test itself finds out by sending a query and expecting
    /// a reply within its own bounded timeout — matching the same "existence check only" scope
    /// <see cref="IsTcpReachableAsync"/> has for a TCP host:port.
    /// </summary>
    public static bool IsSerialPortAvailable(string portName) =>
        SerialPort.GetPortNames().Contains(portName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a USB HID device matching <paramref name="devicePath"/> (preferred — the OS's own
    /// per-connection instance path, disambiguating multiple identical-VID/PID devices the same way
    /// <c>SystemHidDevice.Open</c> does) or, when <paramref name="devicePath"/> is blank, just
    /// <paramref name="vendorId"/>/<paramref name="productId"/>, is currently enumerated.
    /// </summary>
    public static bool IsHidDeviceAvailable(int vendorId, int productId, string? devicePath = null)
    {
        var candidates = DeviceList.Local.GetHidDevices(vendorId, productId, null, null);
        return string.IsNullOrEmpty(devicePath)
            ? candidates.Any()
            : candidates.Any(d => string.Equals(d.DevicePath, devicePath, StringComparison.Ordinal));
    }
}
