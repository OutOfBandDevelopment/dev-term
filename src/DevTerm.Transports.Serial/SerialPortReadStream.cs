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
/// Waiting on the event instead needs no polling and no background work item at all: the wait is
/// a plain <see cref="TaskCompletionSource"/> that a <see cref="CancellationToken"/> registration
/// can cancel directly, and the actual <see cref="SerialPort.Read(byte[],int,int)"/> call only
/// happens once data is already known to be buffered, so it returns immediately.
/// </summary>
internal sealed class SerialPortReadStream(SerialPort port) : Stream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (port.BytesToRead == 0)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnDataReceived(object sender, SerialDataReceivedEventArgs e) => tcs.TrySetResult();

            port.DataReceived += OnDataReceived;
            try
            {
                using var registration = cancellationToken.Register(
                    static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                    tcs);

                // Re-check: bytes may have arrived between the check above and subscribing.
                if (port.BytesToRead == 0)
                {
                    await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                port.DataReceived -= OnDataReceived;
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
