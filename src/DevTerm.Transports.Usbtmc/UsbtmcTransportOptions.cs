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

    /// <summary>
    /// The device's physical USB location (<see cref="UsbtmcDeviceLocation"/>, e.g. <c>usb:1-4.2</c>)
    /// - disambiguates identical devices that have no serial number. Optional, and only consulted
    /// when <see cref="SerialNumber"/> is blank: a serial number stays valid when the device moves
    /// to another port, a location doesn't.
    /// </summary>
    public string? DevicePath { get; set; }

    /// <summary>
    /// Milliseconds a bulk-IN read blocks before timing out. A query whose reply takes longer
    /// than this (a slow DMM integration, <c>*TST?</c>) is aborted and reported as a timeout.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ReadTimeoutMs { get; set; } = 1000;

    /// <summary>Milliseconds a bulk-OUT write blocks before timing out.</summary>
    [Range(1, int.MaxValue)]
    public int WriteTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Payload bytes one bulk-IN read has room for (the read buffer is this plus the 12-byte
    /// header, rounded up to a whole number of the endpoint's packets). A transfer longer than
    /// this is still read, just across several reads - see docs/design/usbtmc-transport.md's
    /// <c>ITransport</c> mapping section.
    /// </summary>
    [Range(1, 16 * 1024 * 1024)]
    public int MaxTransferSize { get; set; } = 65536;

    /// <summary>
    /// Upper bound on a single logical response's total size. A well-formed but implausible
    /// declared TransferSize (firmware bug, not framing corruption) is otherwise
    /// indistinguishable from a legitimately large reply.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxResponseSize { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Send INITIATE_CLEAR (USBTMC 1.0 section 4.2.1.6) right after opening, discarding any reply
    /// a previous session left queued in the device. Best-effort - a device that rejects it still
    /// opens. Off by default: it's reported to hang some Rigol firmware (DS1000Z, python-usbtmc
    /// PR #62), and neither the Linux usbtmc driver nor pyvisa-py sends it on open. The transport
    /// still sends it as a fallback when an abort fails.
    /// </summary>
    public bool ClearOnOpen { get; set; }

    /// <summary>
    /// Assert USB488 REN (REN_CONTROL) on open and return the device to local (GO_TO_LOCAL,
    /// then de-assert REN) on close. Only sent if the device advertises REN_CONTROL support and
    /// isn't known to mishandle it (<see cref="UsbtmcDeviceQuirks.SupportsRemoteControl"/>).
    /// </summary>
    public bool RemoteOnOpen { get; set; } = true;

    /// <summary>
    /// Milliseconds to wait between sending a query and sending the REQUEST_DEV_DEP_MSG_IN that
    /// reads its reply. Null (the default) uses the device's known quirk, if any
    /// (<see cref="UsbtmcDeviceQuirks.RequestDelayMs"/> - a Rigol DG1022 never answers without
    /// one), otherwise 0.
    /// </summary>
    [Range(0, 10_000)]
    public int? RequestDelayMs { get; set; }

    /// <summary>
    /// Send CLEAR_FEATURE(ENDPOINT_HALT) on both bulk endpoints right after opening, whether or
    /// not they're halted. USB 2.0 section 9.4.5 says this always resets the data toggle to DATA0;
    /// firmware that doesn't reset its own toggle in step then has its next packet silently
    /// discarded by the host. Off by default - confirmed against a real Rigol DG1062Z
    /// (docs/test/2026-09-25-18-03-06.md): with this on, the first reply after every open lost its
    /// first 64-byte packet (12 of 12 opens; `*IDN?` came back as just its last 2 bytes, "\n+"),
    /// with it off 12 of 12 were intact. libsigrok dropped the same call for Rigol 0x1AB1:0x0588
    /// to fix a hang, and neither the Linux usbtmc driver nor pyvisa-py does it. It was originally
    /// added for a DM3000 whose first bulk-OUT write STALLed - that case is still handled, by
    /// WriteBulkOut's clear-halt-and-retry on an actual STALL.
    /// </summary>
    public bool ClearHaltOnOpen { get; set; }
}
