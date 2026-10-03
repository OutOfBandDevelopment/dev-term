using DevTerm.Transports.Tcp;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.LxiDeviceOptions"/>: a LAN instrument found by a front end's own scan
/// (<see cref="LxiDeviceScanner.Scan"/>), with the host and raw-SCPI port that get written into the TCP fields when picked.
/// </summary>
public sealed record LxiDeviceOption(string Display, string Host, int Port)
{
    public static LxiDeviceOption FromDevice(LxiDevice device) =>
        new(device.Display, device.Host, device.ScpiPort > 0 ? device.ScpiPort : 5025);
}
