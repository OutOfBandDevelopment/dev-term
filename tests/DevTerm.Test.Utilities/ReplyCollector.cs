using System.Text;
using System.Threading.Channels;

namespace DevTerm.Test.Utilities;

/// <summary>
/// Assembles one complete device reply from a stream of <c>Session.Output</c> texts (fed into a
/// channel), for the real-hardware tests that pair with <see cref="RawPresenter"/>. A reply can
/// arrive over serial in several reads - each a separate Output item - so taking only the first
/// item logged "H" for an HP 34401A's "HEWLETT-PACKARD,34401A,..." while the test still passed.
/// This waits for the first chunk, then keeps appending chunks until the device has been quiet
/// for <c>quietGap</c>, which also stops a reply's tail from being mistaken for the next
/// command's reply.
/// </summary>
public static class ReplyCollector
{
    public static readonly TimeSpan DefaultQuietGap = TimeSpan.FromMilliseconds(300);

    public static async Task<string> ReadReplyAsync(ChannelReader<string> replies, TimeSpan firstChunkTimeout, TimeSpan quietGap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replies);

        var reply = new StringBuilder(await replies.ReadAsync(cancellationToken).AsTask().WaitAsync(firstChunkTimeout, cancellationToken));

        while (true)
        {
            using var gap = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            gap.CancelAfter(quietGap);
            try
            {
                reply.Append(await replies.ReadAsync(gap.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return reply.ToString();
            }
        }
    }
}
