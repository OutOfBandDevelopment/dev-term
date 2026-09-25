using System.Threading.Channels;
using DevTerm.Test.Utilities;

namespace DevTerm.Console.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ReplyCollectorTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task ReplySplitAcrossChunks_IsAssembledWhole_AndTheNextReplyStaysSeparate()
    {
        var replies = Channel.CreateUnbounded<string>();

        // How an HP 34401A's *IDN? reply really arrived over serial: several reads, each its own Output item.
        replies.Writer.TryWrite("H");
        replies.Writer.TryWrite("EWLETT-PACKARD,34401A");
        replies.Writer.TryWrite(",0,11-5-2\r\n");

        var first = await ReplyCollector.ReadReplyAsync(replies.Reader, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100), TestContext.CancellationToken);

        replies.Writer.TryWrite("+1.00000000E+00\r\n");
        var second = await ReplyCollector.ReadReplyAsync(replies.Reader, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100), TestContext.CancellationToken);

        Assert.AreEqual("HEWLETT-PACKARD,34401A,0,11-5-2\r\n", first);
        Assert.AreEqual("+1.00000000E+00\r\n", second);
    }

    [TestMethod]
    public async Task NoReplyAtAll_TimesOut()
    {
        var replies = Channel.CreateUnbounded<string>();

        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            ReplyCollector.ReadReplyAsync(replies.Reader, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50), TestContext.CancellationToken));
    }
}
