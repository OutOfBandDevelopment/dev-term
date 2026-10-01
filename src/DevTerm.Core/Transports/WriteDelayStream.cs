namespace DevTerm.Core.Transports;

/// <summary>
/// Write-only adapter that paces a write out one byte at a time, with an explicit
/// <see cref="FlushAsync(CancellationToken)"/> after each byte - for a slow device with no FIFO
/// buffer that can't absorb a burst write (a whole line/packet arriving faster than the device can
/// consume it causes dropped or corrupted bytes on the receiving end). <paramref name="delayMilliseconds"/>
/// must be 0 or greater: 0 still writes/flushes one byte at a time with no delay between them
/// (useful on its own for a device that just needs every byte flushed individually), a positive
/// value additionally delays that long between bytes, and no delay follows the last byte of a given
/// <see cref="WriteAsync"/> call so back-to-back calls aren't penalized with one delay too many. A
/// negative delay means "disabled" and is never wrapped in this stream at all - the caller writes
/// the buffer as a single, unpaced call instead (see each transport's <c>OpenAsync</c>/<c>WriteAsync</c>).
/// Transport-agnostic - wraps whatever writable <see cref="Stream"/> a transport's write path
/// already uses, the same way <c>XonXoffReadStream</c>/<c>Rfc2217TelnetReadStream</c> wrap a
/// transport's stream for one orthogonal concern, but on the write side instead of read. Has no
/// effect on the read side of a connection - it's a write-only decorator.
/// </summary>
public sealed class WriteDelayStream : Stream
{
    private readonly Stream _inner;
    private readonly int _delayMilliseconds;

    public WriteDelayStream(Stream inner, int delayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);

        _inner = inner;
        _delayMilliseconds = delayMilliseconds;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            await _inner.WriteAsync(buffer.Slice(i, 1), cancellationToken).ConfigureAwait(false);
            await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (_delayMilliseconds > 0 && i < buffer.Length - 1)
            {
                await Task.Delay(_delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
