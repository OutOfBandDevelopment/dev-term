using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.Transports;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class WriteDelayStreamTests
{
    /// <summary>Records each write/flush call instead of actually doing I/O, so tests can assert exact call shape.</summary>
    private sealed class RecordingStream : Stream
    {
        public List<byte[]> Writes { get; } = [];

        public int FlushCount { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes.Add(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return Task.CompletedTask;
        }

        public override void Flush() => FlushCount++;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [TestMethod]
    public void Constructor_WithNegativeDelay_Throws()
    {
        using var inner = new RecordingStream();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WriteDelayStream(inner, -1));
    }

    [TestMethod]
    public async Task WriteAsync_WithZeroDelay_WritesAndFlushesOneByteAtATimeWithNoDelay()
    {
        using var inner = new RecordingStream();
        using var stream = new WriteDelayStream(inner, delayMilliseconds: 0);

        var start = DateTime.UtcNow;
        await stream.WriteAsync(new byte[] { 1, 2, 3 }, TestContext.CancellationToken);
        var elapsed = DateTime.UtcNow - start;

        Assert.AreEqual(3, inner.Writes.Count, "one write call per byte");
        Assert.IsTrue(inner.Writes.All(w => w.Length == 1), "each write is exactly one byte");
        Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, inner.Writes.Select(w => w[0]));
        Assert.AreEqual(3, inner.FlushCount);
        Assert.IsTrue(elapsed < TimeSpan.FromSeconds(1), $"Expected no delay, took {elapsed}.");
    }

    [TestMethod]
    public async Task WriteAsync_WithPositiveDelay_DelaysBetweenBytesButNotAfterTheLastOne()
    {
        using var inner = new RecordingStream();
        using var stream = new WriteDelayStream(inner, delayMilliseconds: 100);

        var start = DateTime.UtcNow;
        await stream.WriteAsync(new byte[] { 1, 2, 3 }, TestContext.CancellationToken);
        var elapsed = DateTime.UtcNow - start;

        Assert.AreEqual(3, inner.Writes.Count, "one write call per byte");
        Assert.IsTrue(inner.Writes.All(w => w.Length == 1), "each write is exactly one byte");
        Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, inner.Writes.Select(w => w[0]));
        Assert.AreEqual(3, inner.FlushCount);

        // Two gaps (after byte 1, after byte 2), none after byte 3.
        Assert.IsTrue(elapsed >= TimeSpan.FromMilliseconds(180), $"Expected at least two ~100ms delays, took {elapsed}.");
        Assert.IsTrue(elapsed < TimeSpan.FromMilliseconds(500), $"Expected no third delay after the last byte, took {elapsed}.");
    }

    [TestMethod]
    public async Task WriteAsync_WithSingleByte_NeverDelays()
    {
        using var inner = new RecordingStream();
        using var stream = new WriteDelayStream(inner, delayMilliseconds: 1000);

        var start = DateTime.UtcNow;
        await stream.WriteAsync(new byte[] { 1 }, TestContext.CancellationToken);
        var elapsed = DateTime.UtcNow - start;

        Assert.AreEqual(1, inner.Writes.Count, "one write call per byte");
        Assert.AreSequenceEqual(new byte[] { 1 }, inner.Writes.Select(w => w[0]));
        Assert.IsTrue(elapsed < TimeSpan.FromSeconds(1), $"Expected no delay after the only byte, took {elapsed}.");
    }

    [TestMethod]
    public void Flush_DelegatesToInner()
    {
        using var inner = new RecordingStream();
        using var stream = new WriteDelayStream(inner, delayMilliseconds: 0);

        stream.Flush();

        Assert.AreEqual(1, inner.FlushCount);
    }

    [TestMethod]
    public void CanRead_CanSeek_AreFalse_CanWrite_IsTrue()
    {
        using var inner = new RecordingStream();
        using var stream = new WriteDelayStream(inner, delayMilliseconds: 0);

        Assert.IsFalse(stream.CanRead);
        Assert.IsFalse(stream.CanSeek);
        Assert.IsTrue(stream.CanWrite);
    }

    [TestMethod]
    public void Read_Throws()
    {
        using var inner = new RecordingStream();
        using var stream = new WriteDelayStream(inner, delayMilliseconds: 0);

        Assert.ThrowsExactly<NotSupportedException>(() => stream.Read(new byte[1], 0, 1));
    }

    [TestMethod]
    public void Write_Throws()
    {
        using var inner = new RecordingStream();
        using var stream = new WriteDelayStream(inner, delayMilliseconds: 0);

        Assert.ThrowsExactly<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
    }

    public required TestContext TestContext { get; set; }
}
