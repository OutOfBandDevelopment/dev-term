using System.ComponentModel.DataAnnotations;

namespace DevTerm.Transports.Vxi11;

/// <summary>
/// Configuration for a VXI-11 (ONC-RPC core channel) session with an LXI instrument. Bound via the
/// Options pattern. See docs/design/vxi11-transport.md.
/// </summary>
public sealed class Vxi11TransportOptions
{
    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    /// <summary>The core channel's TCP port, or 0 (the default) to ask the instrument's portmapper on TCP 111.</summary>
    [Range(0, 65535)]
    public int Port { get; set; }

    /// <summary>The logical device name given to create_link; <c>inst0</c> is what virtually every instrument serves.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Device { get; set; } = "inst0";

    /// <summary>Bounds a write (and the connect handshake) so it fails with a <see cref="TimeoutException"/>. In milliseconds.</summary>
    public int WriteTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// How long each device_read waits for the instrument before reporting nothing yet, in
    /// milliseconds. The core channel is request/response, so a write waits behind a read in
    /// progress; keep this short. Code-only.
    /// </summary>
    [Range(0, 10000)]
    public int ReadPollMs { get; set; } = 50;

    /// <summary>
    /// Append a line feed to a reply the instrument ended with the END flag but without a line
    /// terminator, so a line-buffered presenter flushes it. Most instruments already send one.
    /// </summary>
    public bool AppendLineFeedAtEnd { get; set; } = true;
}
