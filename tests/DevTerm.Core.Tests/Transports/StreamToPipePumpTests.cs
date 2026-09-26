using System.IO.Pipelines;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.Transports;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class StreamToPipePumpTests
{
    /// <summary>A read-only stream whose <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> always throws.</summary>
    private sealed class ThrowingReadStream(Exception exception, Action? beforeThrow = null) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            beforeThrow?.Invoke();
            throw exception;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task RunAsync_WhenTheStreamThrowsDuringARead_CompletesTheWriterWithThatExceptionInsteadOfACleanHangUp()
    {
        var pipe = new Pipe();
        var thrown = new IOException("The device closed the connection.");
        using var stream = new ThrowingReadStream(thrown);

        await StreamToPipePump.RunAsync(stream, pipe.Writer, CancellationToken.None);

        var observed = await Assert.ThrowsExactlyAsync<IOException>(() => pipe.Reader.ReadAsync().AsTask());
        Assert.AreSame(thrown, observed);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task RunAsync_WhenCancelledDuringARead_CompletesTheWriterWithNoErrorAsACleanClose()
    {
        var pipe = new Pipe();
        using var cts = new CancellationTokenSource();
        using var stream = new ThrowingReadStream(new OperationCanceledException(), beforeThrow: cts.Cancel);

        await StreamToPipePump.RunAsync(stream, pipe.Writer, cts.Token);

        var result = await pipe.Reader.ReadAsync();
        Assert.IsTrue(result.IsCompleted);
    }

    /// <summary>A read-only stream that hands back <paramref name="data"/> exactly once.</summary>
    private sealed class OneShotReadStream(byte[] data, Action? beforeReturn = null) : Stream
    {
        private bool _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_served)
            {
                throw new InvalidOperationException("ReadAsync called more than once.");
            }

            _served = true;
            beforeReturn?.Invoke();
            data.CopyTo(buffer);
            return ValueTask.FromResult(data.Length);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public async Task RunAsync_WhenTheTokenIsCancelledBeforeTheFlushThatFollowsARead_CompletesWithoutThrowing()
    {
        var pipe = new Pipe();
        using var cts = new CancellationTokenSource();
        using var stream = new OneShotReadStream([1, 2, 3, 4], beforeReturn: cts.Cancel);

        await StreamToPipePump.RunAsync(stream, pipe.Writer, cts.Token);

        var result = await pipe.Reader.ReadAsync();
        Assert.IsTrue(result.IsCompleted);
    }
}
