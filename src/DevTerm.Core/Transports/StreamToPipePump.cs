using System.IO.Pipelines;

namespace DevTerm.Core.Transports;

/// <summary>
/// Pumps bytes from a readable <see cref="Stream"/> into a <see cref="PipeWriter"/> without an
/// intermediate array allocation per read: each read lands directly in a buffer rented from the
/// pipe's pool via <see cref="PipeWriter.GetMemory"/>. Shared by any transport whose underlying
/// I/O is stream-shaped (serial, TCP, ...). See docs/design/platform.md.
///
/// Relies on <paramref name="source"/>'s <c>ReadAsync</c> properly honoring
/// <paramref name="cancellationToken"/> to notice a close/cancel promptly. Not every stream does
/// this reliably (verified against real hardware that <see cref="System.IO.Ports.SerialPort"/>'s
/// <c>BaseStream</c> does not) — such a transport should wrap its stream so cancellation works
/// before handing it to this pump, rather than this shared helper working around one stream's
/// quirk. See <c>DevTerm.Transports.Serial.CancellableReadStream</c>.
/// </summary>
public static class StreamToPipePump
{
    private const int _minimumBufferSize = 4096;

    public static async Task RunAsync(Stream source, PipeWriter writer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);

        Exception? error = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var memory = writer.GetMemory(_minimumBufferSize);

                int bytesRead;
                try
                {
                    bytesRead = await source.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected: the transport is deliberately closing and cancelled the read.
                    break;
                }
                catch (Exception ex)
                {
                    // A real read failure (TCP reset, USB-serial unplug) - the writer completes
                    // with this exception so Session.PumpAsync's ReadAsync surfaces it instead of
                    // reporting a clean, error-less hang-up.
                    error = ex;
                    break;
                }

                if (bytesRead == 0)
                {
                    break;
                }

                writer.Advance(bytesRead);

                FlushResult flushResult;
                try
                {
                    flushResult = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected: a paused pipe (backpressure) blocks FlushAsync until either a
                    // reader catches up or the transport deliberately closes and cancels us.
                    break;
                }

                if (flushResult.IsCompleted || flushResult.IsCanceled)
                {
                    break;
                }
            }
        }
        finally
        {
            await writer.CompleteAsync(error).ConfigureAwait(false);
        }
    }
}
