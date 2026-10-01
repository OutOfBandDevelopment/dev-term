using System.Buffers;
using System.IO.Pipelines;

namespace DevTerm.Transports.Rfc2217.Tests;

/// <summary>
/// A real, in-memory full-duplex stream for tests: reads come from one <see cref="Pipe"/> (what
/// the test — standing in for the remote peer — writes to simulate incoming wire bytes), writes go
/// to another (what the test reads back to inspect what the code under test sent). Mirrors a real
/// <see cref="System.Net.Sockets.NetworkStream"/>'s shape — one stream, both directions — without a
/// socket, the same way <c>DevTerm.Transports.Tcp.Tests</c>' single-direction fake stream does for
/// <c>TcpTransport</c> (which doesn't need the write side of that fake, since it writes through
/// <c>ITcpConnection.WriteAsync</c> directly instead of through the stream).
/// </summary>
internal sealed class DuplexPipeStream(Pipe incoming, Pipe outgoing) : Stream
{
    private readonly PipeReader _incomingReader = incoming.Reader;
    private readonly PipeWriter _outgoingWriter = outgoing.Writer;

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var result = await _incomingReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var slice = result.Buffer;
        if (slice.IsEmpty && result.IsCompleted)
        {
            _incomingReader.AdvanceTo(slice.End);
            return 0;
        }

        var n = Math.Min(buffer.Length, (int)slice.Length);
        slice.Slice(0, n).CopyTo(buffer.Span);
        _incomingReader.AdvanceTo(slice.GetPosition(n));
        return n;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _outgoingWriter.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
