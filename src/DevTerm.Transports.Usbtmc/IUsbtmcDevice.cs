namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// Thin abstraction over a USBTMC device's control/bulk endpoints so <see cref="UsbtmcTransport"/>
/// can be unit tested with a fake/mock instead of real USB hardware. Exposes whole, already-framed
/// USBTMC bulk transfers rather than raw endpoint bytes - the header codec (see
/// <see cref="UsbtmcCodec"/>) is the transport's job, not the device's.
/// </summary>
public interface IUsbtmcDevice : IDisposable
{
    bool IsOpen { get; }

    /// <summary>The largest single bulk transfer this device will be asked to send/receive at a time.</summary>
    int MaxTransferSize { get; }

    void Open();

    void Close();

    /// <summary>Writes one already-framed USBTMC bulk-OUT transfer (header + padded payload).</summary>
    void WriteBulkOut(byte[] transfer);

    /// <summary>
    /// Reads one bulk-IN transfer into <paramref name="buffer"/>, returning the number of bytes
    /// actually read, or 0 if the read timed out or came back an empty transfer with nothing
    /// available. <paramref name="stalled"/> reports whether the endpoint STALLed and was cleared
    /// and retried internally (even when this call still returns data) - a caller that was reading
    /// the start of a fresh reply should treat that as a sign the device may have discarded the
    /// bulk-OUT request this read was meant to answer, not just a slow response, and re-send it
    /// rather than only retrying the read - see <see cref="UsbtmcTransport"/>.
    /// </summary>
    int ReadBulkIn(byte[] buffer, out bool stalled);

    /// <summary>
    /// Best-effort USB488 subclass REN_CONTROL (<paramref name="remote"/> true) / GO_TO_LOCAL
    /// (false) control request. A no-op if the device doesn't implement the USB488 subclass
    /// extension - not every USBTMC device does, and this isn't required for basic query/reply.
    /// </summary>
    void SetRemote(bool remote);
}
