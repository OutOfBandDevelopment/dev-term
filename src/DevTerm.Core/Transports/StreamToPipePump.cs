using System.IO.Pipelines;

namespace DevTerm.Core.Transports;

/// <summary>
/// Pumps bytes from a readable <see cref="Stream"/> into a <see cref="PipeWriter"/> without an
/// intermediate array allocation per read: each read lands directly in a buffer rented from the
/// pipe's pool via <see cref="PipeWriter.GetMemory"/>. Shared by any transport whose underlying
/// I/O is stream-shaped (serial, TCP, ...). See docs/design/platform.md.
/// </summary>
public static class StreamToPipePump
{
    private const int MinimumBufferSize = 4096;

    public static async Task RunAsync(Stream source, PipeWriter writer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var memory = writer.GetMemory(MinimumBufferSize);

                int bytesRead;
                try
                {
                    bytesRead = await source.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
                {
                    break;
                }

                if (bytesRead == 0)
                {
                    break;
                }

                writer.Advance(bytesRead);

                var flushResult = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (flushResult.IsCompleted || flushResult.IsCanceled)
                {
                    break;
                }
            }
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }
}
