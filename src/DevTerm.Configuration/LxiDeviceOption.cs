using DevTerm.Transports.Tcp;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.LxiDeviceOptions"/>: a LAN instrument found by a front end's own scan
/// (<see cref="LxiDeviceScanner.Scan"/>), with the host and raw-SCPI port that get written into the TCP fields when picked.
/// </summary>
public sealed record LxiDeviceOption(string Display, string Host, int Port)
{
    /// <summary>A hit from any probe; only a plain <c>tcp</c> hit carries a usable port, a VXI-11-only one falls back to 5025 like before.</summary>
    public static LxiDeviceOption FromHit(Discovery.NetworkDeviceHit hit) =>
        new(hit.Display, hit.Address, hit.Transport == "tcp" ? hit.Port : 5025);

    public static LxiDeviceOption FromDevice(LxiDevice device) =>
        new(device.Display, device.Host, device.ScpiPort > 0 ? device.ScpiPort : 5025);
}
