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

    /// <summary>
    /// The bulk-IN endpoint's wMaxPacketSize. A bulk-IN read shorter than the buffer it was given
    /// means the device terminated the transfer with a short packet (USBTMC 1.0 section 3.3 rule
    /// 10), so the transport sizes its read buffers as a multiple of this - a buffer that isn't a
    /// multiple can't hold a full packet at its tail, and libusb reports that as an overflow.
    /// </summary>
    int MaxPacketSize { get; }

    void Open();

    void Close();

    /// <summary>
    /// Writes one already-framed USBTMC bulk-OUT transfer (header + padded payload). Throws
    /// <see cref="TimeoutException"/> if the device didn't accept it within the write timeout.
    /// </summary>
    void WriteBulkOut(byte[] transfer);

    /// <summary>
    /// Reads one bulk-IN transfer into <paramref name="buffer"/>, returning the number of bytes
    /// actually read - 0 is a real zero-length packet, not a timeout. Throws
    /// <see cref="TimeoutException"/> if nothing arrived within the read timeout.
    /// <paramref name="stalled"/> reports whether the endpoint STALLed and was cleared and retried
    /// internally; when it's true a timeout on that retry returns 0 instead of throwing. A caller
    /// that was reading the start of a fresh reply should treat a stall as a sign the device may
    /// have discarded the bulk-OUT request this read was meant to answer, and re-send it rather
    /// than only retrying the read - see <see cref="UsbtmcTransport"/>.
    /// </summary>
    int ReadBulkIn(byte[] buffer, out bool stalled);

    /// <summary>
    /// Best-effort INITIATE_ABORT_BULK_IN / CHECK_ABORT_BULK_IN_STATUS sequence (USBTMC 1.0
    /// section 4.2.1.4) for the transfer tagged <paramref name="bTag"/>, draining the bulk-IN
    /// endpoint until a short packet so the next query starts on a fresh header.
    /// </summary>
    void AbortBulkIn(byte bTag);

    /// <summary>
    /// Best-effort INITIATE_ABORT_BULK_OUT / CHECK_ABORT_BULK_OUT_STATUS sequence (USBTMC 1.0
    /// section 4.2.1.2) for the transfer tagged <paramref name="bTag"/>, then clears the bulk-OUT halt.
    /// </summary>
    void AbortBulkOut(byte bTag);

    /// <summary>
    /// Best-effort INITIATE_CLEAR / CHECK_CLEAR_STATUS sequence (USBTMC 1.0 section 4.2.1.6):
    /// discards everything pending in the device's input and output buffers.
    /// </summary>
    void Clear();

    /// <summary>
    /// Best-effort USB488 subclass remote/local control: REN_CONTROL with wValue=1 for
    /// <paramref name="remote"/> true; GO_TO_LOCAL then REN_CONTROL with wValue=0 for false.
    /// A no-op if the device doesn't advertise USB488 REN_CONTROL support in GET_CAPABILITIES -
    /// not every USBTMC device does, and this isn't required for basic query/reply.
    /// </summary>
    void SetRemote(bool remote);
}
