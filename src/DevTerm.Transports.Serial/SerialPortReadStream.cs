using System.IO.Ports;
using System.Runtime.InteropServices;

namespace DevTerm.Transports.Serial;

/// <summary>
/// Read-side adapter over a real <see cref="SerialPort"/> that waits for data via the
/// <see cref="SerialPort.DataReceived"/> event instead of a blocking read.
///
/// Two approaches were tried and rejected first, against real hardware:
/// <list type="bullet">
/// <item><description><c>BaseStream.ReadAsync(Memory, CancellationToken)</c> directly — doesn't
/// reliably honor the cancellation token on an in-flight read on this driver, so Close/Ctrl+C
/// hung indefinitely.</description></item>
/// <item><description>Wrapping the classic synchronous <c>Read</c> (which does honor
/// <see cref="SerialPort.ReadTimeout"/>) in <c>Task.Run</c> and polling cancellation between
/// timeout-bounded attempts — cancels within one timeout window, but that background work item
/// is not itself cancelable: if anything ever stops awaiting it early, it keeps running
/// unobserved and can fault later with nobody watching, which is exactly what an
/// UnobservedTaskException looks like.</description></item>
/// </list>
/// Waiting on the event instead needs no background work item at all: the wait is a plain
/// <see cref="TaskCompletionSource"/> that a <see cref="CancellationToken"/> registration can
/// cancel directly, and the actual <see cref="SerialPort.Read(byte[],int,int)"/> call only
/// happens once data is already known to be buffered, so it returns immediately. A slow
/// (1s) <see cref="SerialPort.BytesToRead"/> poll runs alongside the event wait purely as an
/// unplug-detection fallback - see the remarks inside <see cref="ReadAsync"/> and
/// docs/bugs/fixed/057-serial-unplug-not-detected.md.
/// </summary>
internal sealed class SerialPortReadStream(SerialPort port) : Stream
{
    private static readonly TimeSpan _unplugPollInterval = TimeSpan.FromSeconds(1);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (port.BytesToRead == 0)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnDataReceived(object sender, SerialDataReceivedEventArgs e) => tcs.TrySetResult();
            void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e) => tcs.TrySetResult();

            port.DataReceived += OnDataReceived;
            port.ErrorReceived += OnErrorReceived;
            try
            {
                using var registration = cancellationToken.Register(
                    static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                    tcs);

                // Re-check: bytes may have arrived between the check above and subscribing.
                //
                // Neither DataReceived nor ErrorReceived is guaranteed to fire for a physical
                // unplug with no data in flight - a yanked USB-serial adapter often raises
                // neither, so waiting on them alone can hang forever (see
                // docs/bugs/fixed/057-serial-unplug-not-detected.md). Poll BytesToRead on an
                // interval as a fallback instead of (or in addition to) trusting either event:
                // BytesToRead touches the real device handle, so it throws on a genuinely removed
                // port the same way the eventual Read() below would - IsOpen was considered
                // instead but doesn't help here, since it only reflects whether Close() was
                // called, not whether the underlying device is still physically present.
                while (port.BytesToRead == 0 && !tcs.Task.IsCompleted)
                {
                    await Task.WhenAny(tcs.Task, Task.Delay(_unplugPollInterval, cancellationToken)).ConfigureAwait(false);
                }

                if (tcs.Task.IsCompleted)
                {
                    // Observes a cancellation or a benign DataReceived/ErrorReceived completion;
                    // a real removal surfaces instead from the BytesToRead/Read calls themselves.
                    await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                port.DataReceived -= OnDataReceived;
                port.ErrorReceived -= OnErrorReceived;
            }
        }

        if (!MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
        {
            // Pipe-backed buffers (the only kind StreamToPipePump hands us) are always array-backed.
            throw new InvalidOperationException("Expected an array-backed buffer.");
        }

        return port.Read(segment.Array!, segment.Offset, segment.Count);
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

    public override int Read(byte[] buffer, int offset, int count) => port.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
