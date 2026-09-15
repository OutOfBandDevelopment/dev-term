using System.Threading.Channels;
using HidSharp;

namespace DevTerm.Transports.Hid;

/// <summary>
/// Read-side adapter over a real <see cref="HidStream"/>. HidSharp gives no event analogous to
/// <see cref="System.IO.Ports.SerialPort.DataReceived"/> to wait on for "a report is ready" (see
/// <c>DevTerm.Transports.Serial.SerialPortReadStream</c> for why that's the preferred shape when
/// a transport does have one), and .NET's default <c>Stream.ReadAsync</c> bridging over a
/// <c>BeginRead</c>/<c>EndRead</c>-only implementation (which is what <see cref="HidStream"/>
/// provides) does not meaningfully honor a <see cref="CancellationToken"/> once a read is already
/// in flight — the same class of problem already hit and fixed for
/// <see cref="System.IO.Ports.SerialPort"/>.
///
/// Instead of a Task.Run-wrapped blocking read per call (rejected for serial: the background work
/// item isn't itself cancelable, so it keeps running unobserved past the point anyone's waiting on
/// it, and can fault later with nobody watching), this owns a single dedicated background
/// <see cref="Thread"/> for the life of the connection. It blocks on <see cref="HidStream.Read(byte[])"/>
/// (bounded by <see cref="HidStream.ReadTimeout"/>, so it periodically notices <see cref="Stop"/>
/// instead of blocking forever) and hands each completed report to a <see cref="Channel{T}"/>.
/// <see cref="ReadAsync"/> only ever awaits <see cref="ChannelReader{T}.WaitToReadAsync"/> — a real,
/// immediately-cancelable async primitive — so a caller's <see cref="CancellationToken"/> is honored
/// right away regardless of whether the background thread's own (bounded, best-effort) shutdown has
/// happened yet. <b>Not yet verified against real HID hardware</b> — see
/// docs/design/proposals/radex-one-protocol.md's open question on report framing.
/// </summary>
internal sealed class HidReadStream : Stream
{
    private readonly HidStream _stream;
    private readonly Thread _readThread;
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly int _readTimeoutMs;
    private volatile bool _stopRequested;
    private byte[]? _pending;
    private int _pendingOffset;

    public HidReadStream(HidStream stream, int readTimeoutMs)
    {
        _stream = stream;
        _readTimeoutMs = readTimeoutMs;
        _stream.ReadTimeout = readTimeoutMs;
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "DevTerm HID read" };
        _readThread.Start();
    }

    private void ReadLoop()
    {
        var buffer = new byte[_stream.Device.GetMaxInputReportLength()];

        try
        {
            while (!_stopRequested)
            {
                int count;
                try
                {
                    count = _stream.Read(buffer);
                }
                catch (TimeoutException)
                {
                    // No report within ReadTimeout - not an error, just a chance to notice Stop().
                    continue;
                }
                catch (Exception)
                {
                    // Device unplugged/faulted/closed - end the stream like end-of-data.
                    break;
                }

                if (count <= 0)
                {
                    break;
                }

                var report = new byte[count];
                Array.Copy(buffer, report, count);
                if (!_channel.Writer.TryWrite(report))
                {
                    break;
                }
            }
        }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }

    /// <summary>Signals the read loop to stop and waits (bounded) for it to actually exit.</summary>
    public void Stop()
    {
        _stopRequested = true;

        // The loop notices within one ReadTimeout window; IsBackground=true means a read that
        // never returns (e.g. the OS/driver itself hangs on physical unplug) can't prevent
        // process exit even if this join times out.
        _readThread.Join(TimeSpan.FromMilliseconds(_readTimeoutMs + 1000));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_pending is null)
        {
            if (!await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)
                || !_channel.Reader.TryRead(out _pending))
            {
                return 0;
            }

            _pendingOffset = 0;
        }

        var toCopy = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
        _pending.AsSpan(_pendingOffset, toCopy).CopyTo(buffer.Span);
        _pendingOffset += toCopy;
        if (_pendingOffset >= _pending.Length)
        {
            _pending = null;
        }

        return toCopy;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
