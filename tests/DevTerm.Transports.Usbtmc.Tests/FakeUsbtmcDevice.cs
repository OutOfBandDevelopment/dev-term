using DevTerm.Transports.Usbtmc;

namespace DevTerm.Transports.Usbtmc.Tests;

/// <summary>
/// Hand-rolled <see cref="IUsbtmcDevice"/> fake driven by a queue of canned physical bulk-IN
/// reads - a plain fake instead of a mocking framework because <see cref="ReadBulkIn"/>'s
/// <c>out bool stalled</c> parameter needs a different value per call in sequence, which Moq's
/// out-parameter support can't express cleanly. A read returning fewer bytes than the buffer it
/// was given is a short packet (the transfer is over), exactly as with real libusb; reading with
/// nothing queued times out.
/// </summary>
internal sealed class FakeUsbtmcDevice : IUsbtmcDevice
{
    private readonly Queue<(byte[]? Data, bool Stalled)> _reads = new();

    public List<byte[]> WrittenFrames { get; } = [];

    public List<byte> AbortedBulkInTags { get; } = [];

    public List<byte> AbortedBulkOutTags { get; } = [];

    public List<bool> RemoteCalls { get; } = [];

    public int ClearCount { get; private set; }

    public bool IsOpen { get; private set; }

    public int MaxTransferSize { get; init; } = 1024;

    public int MaxPacketSize { get; init; } = 4;

    /// <summary>When set, the next bulk-OUT write times out instead of being recorded.</summary>
    public bool TimeOutNextWrite { get; set; }

    public void EnqueueRead(byte[] data, bool stalled = false) => _reads.Enqueue((data, stalled));

    public void EnqueueTimeout() => _reads.Enqueue((null, false));

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public void Dispose()
    {
    }

    public void WriteBulkOut(byte[] transfer)
    {
        if (TimeOutNextWrite)
        {
            TimeOutNextWrite = false;
            throw new TimeoutException("fake bulk-OUT timeout");
        }

        WrittenFrames.Add(transfer);
    }

    public int ReadBulkIn(byte[] buffer, out bool stalled)
    {
        if (!_reads.TryDequeue(out var read) || read.Data is null)
        {
            stalled = false;
            throw new TimeoutException("fake bulk-IN timeout");
        }

        stalled = read.Stalled;
        read.Data.CopyTo(buffer, 0);
        return read.Data.Length;
    }

    public void AbortBulkIn(byte bTag) => AbortedBulkInTags.Add(bTag);

    public void AbortBulkOut(byte bTag) => AbortedBulkOutTags.Add(bTag);

    public void Clear() => ClearCount++;

    public void SetRemote(bool remote) => RemoteCalls.Add(remote);
}
