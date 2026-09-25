using System.Net.Sockets;

namespace DevTerm.Test.Utilities;

/// <summary>
/// Preflight checks for <c>DevLocal</c> tests so a real device left offline (bench powered down,
/// bridge unplugged) degrades to <c>Assert.Inconclusive</c> the same way a missing
/// <c>.runsettings</c> parameter already does, rather than failing or hanging. Shared by
/// <c>DevTerm.Console.Tests.RealHardwareCliTests</c> and
/// <c>DevTerm.Wpf.Tests.RealHardwareMainWindowTests</c> — any future serial/USB real-hardware test
/// belongs here too (a COM port or USB vendor/product ID existence check), alongside this TCP one,
/// rather than being duplicated per test project.
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
}
