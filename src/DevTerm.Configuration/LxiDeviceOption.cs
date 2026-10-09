using DevTerm.Transports.Tcp;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.LxiDeviceOptions"/>: a LAN instrument found by a front end's own scan
/// (<see cref="LxiDeviceScanner.Scan"/>), with the host and raw-SCPI port that get written into the TCP fields when picked.
/// </summary>
public sealed record LxiDeviceOption(string Display, string Host, int Port, string Transport = "tcp")
{
    /// <summary>
    /// A hit from any probe, carrying the transport it suggests: <c>vxi11</c> and <c>mqtt</c> hits switch the editor to that
    /// transport; anything else is a plain <c>tcp</c> hit (a VXI-11-only hit's RPC port is not a raw-SCPI port, so none is kept).
    /// </summary>
    public static LxiDeviceOption FromHit(Discovery.NetworkDeviceHit hit) => hit.Transport switch
    {
        "vxi11" => new(hit.Display, hit.Address, 0, "vxi11"),
        "mqtt" => new(hit.Display, hit.Address, hit.Port, "mqtt"),
        "tcp" => new(hit.Display, hit.Address, hit.Port),
        _ => new(hit.Display, hit.Address, hit.Port),
    };

    public static LxiDeviceOption FromDevice(LxiDevice device) =>
        new(device.Display, device.Host, device.ScpiPort > 0 ? device.ScpiPort : 5025);
}
