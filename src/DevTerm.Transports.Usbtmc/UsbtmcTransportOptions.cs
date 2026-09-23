using System.ComponentModel.DataAnnotations;

namespace DevTerm.Transports.Usbtmc;

/// <summary>Configuration for a USBTMC device session. Bound via the Options pattern.</summary>
public sealed class UsbtmcTransportOptions
{
    /// <summary>USB Vendor ID, decimal (Windows Device Manager shows this in hex - convert before setting this).</summary>
    [Range(1, 0xFFFF)]
    public int VendorId { get; set; }

    /// <summary>USB Product ID, decimal (Windows Device Manager shows this in hex - convert before setting this).</summary>
    [Range(1, 0xFFFF)]
    public int ProductId { get; set; }

    /// <summary>Disambiguates when more than one connected device matches VendorId/ProductId. Optional.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Milliseconds a bulk-IN read blocks before timing out.</summary>
    public int ReadTimeoutMs { get; set; } = 1000;

    /// <summary>Milliseconds a bulk-OUT write blocks before timing out.</summary>
    public int WriteTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// The largest single bulk transfer requested/accepted at a time. Also sizes the read buffer
    /// used to reassemble a reply that spans multiple bulk-IN transfers - see
    /// docs/design/usbtmc-transport.md's <c>ITransport</c> mapping section.
    /// </summary>
    public int MaxTransferSize { get; set; } = 65536;
}
