namespace DevTerm.Transports.Serial;

/// <summary>
/// Wraps a stream whose <c>ReadAsync(Memory&lt;byte&gt;, CancellationToken)</c> doesn't reliably
/// honor cancellation on an in-flight read — verified against real hardware for
/// <see cref="System.IO.Ports.SerialPort"/>'s <c>BaseStream</c> — by falling back to the classic
/// synchronous <see cref="Stream.Read(Span{byte})"/>, which does reliably respect
/// <see cref="Stream.ReadTimeout"/>, and re-checking the caller's cancellation token between
/// attempts. This is what lets <see cref="DevTerm.Core.Transports.StreamToPipePump"/> (and hence
/// Close/Ctrl+C) notice cancellation promptly instead of blocking until the next byte arrives.
/// </summary>
internal sealed class CancellableReadStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await Task.Run(() => inner.Read(buffer.Span), CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // No data within inner's ReadTimeout — not an error, just re-check cancellation and retry.
            }
        }
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
