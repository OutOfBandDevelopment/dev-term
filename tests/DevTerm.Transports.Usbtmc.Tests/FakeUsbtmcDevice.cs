using DevTerm.Transports.Usbtmc;

namespace DevTerm.Transports.Usbtmc.Tests;

/// <summary>
/// Hand-rolled <see cref="IUsbtmcDevice"/> fake driven by a queue of canned physical bulk-IN
/// transfers - a plain fake instead of a mocking framework because <see cref="ReadBulkIn"/>'s
/// <c>out bool stalled</c> parameter needs a different value per call in sequence, which Moq's
/// out-parameter support can't express cleanly.
/// </summary>
internal sealed class FakeUsbtmcDevice : IUsbtmcDevice
{
    private readonly Queue<(byte[] Data, bool Stalled)> _reads = new();

    public List<byte[]> WrittenFrames { get; } = new();

    public bool IsOpen { get; private set; }

    public int MaxTransferSize { get; init; } = 1024;

    public void EnqueueRead(byte[] data, bool stalled = false) => _reads.Enqueue((data, stalled));

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public void Dispose()
    {
    }

    public void WriteBulkOut(byte[] transfer) => WrittenFrames.Add(transfer);

    public int ReadBulkIn(byte[] buffer, out bool stalled)
    {
        if (_reads.Count == 0)
        {
            stalled = false;
            return 0;
        }

        var (data, wasStalled) = _reads.Dequeue();
        stalled = wasStalled;
        data.CopyTo(buffer, 0);
        return data.Length;
    }

    public void SetRemote(bool remote)
    {
    }
}
