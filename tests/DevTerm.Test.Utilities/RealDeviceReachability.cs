using System.IO.Ports;
using System.Net.Sockets;
using DevTerm.Transports.Usbtmc;
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

    /// <summary>
    /// Whether a USBTMC device matching <paramref name="vendorId"/>/<paramref name="productId"/> —
    /// and, when <paramref name="serialNumber"/> is non-blank, that exact serial number too — is
    /// currently enumerated. A serial number match matters on at least one real bench: a Rigol
    /// DS1102E and a Rigol DG1022 enumerate under the exact same VID:PID there (see
    /// rigol-ds1102e.json's Notes and BACKLOG.md's USBTMC entry), so VID:PID alone can't tell them
    /// apart. Uses the same <see cref="SystemUsbtmcDeviceDiscovery"/> libusb enumeration
    /// <see cref="UsbtmcTransport"/> itself opens through, rather than re-deriving the USBTMC
    /// interface-class filter here.
    /// </summary>
    public static bool IsUsbtmcDeviceAvailable(int vendorId, int productId, string? serialNumber = null)
    {
        var candidates = new SystemUsbtmcDeviceDiscovery().GetDevices()
            .Where(d => d.VendorId == vendorId && d.ProductId == productId);
        return string.IsNullOrEmpty(serialNumber)
            ? candidates.Any()
            : candidates.Any(d => string.Equals(d.SerialNumber, serialNumber, StringComparison.Ordinal));
    }
}
