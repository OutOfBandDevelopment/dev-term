using System.ComponentModel.DataAnnotations;
using System.IO.Ports;

namespace DevTerm.Transports.Serial;

/// <summary>Configuration for a serial/UART session. Bound via the Options pattern.</summary>
public sealed class SerialTransportOptions
{
    [Required(AllowEmptyStrings = false)]
    public string PortName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int BaudRate { get; set; } = 9600;

    [Range(5, 8)]
    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public Handshake Handshake { get; set; } = Handshake.None;
}
