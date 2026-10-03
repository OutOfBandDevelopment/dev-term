using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.StreamContent;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DevTerm.Core.Tests.StreamContent;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class StreamContentWatcherTests
{
    private static readonly TimeSpan _idle = TimeSpan.FromSeconds(2);

    public TestContext TestContext { get; set; } = null!;

    private static (StreamContentWatcher Watcher, FakeTimeProvider Time, List<StreamCapture> Captures) Create(int maxCaptureBytes = 64 * 1024 * 1024)
    {
        var time = new FakeTimeProvider();
        var watcher = new StreamContentWatcher(new StreamContentWatcherOptions { IdleTimeout = _idle, MaxCaptureBytes = maxCaptureBytes }, time);
        var captures = new List<StreamCapture>();
        watcher.ContentDetected += (_, capture) => captures.Add(capture);
        return (watcher, time, captures);
    }

    private static void Feed(StreamContentWatcher watcher, byte[] data, int chunkSize = int.MaxValue)
    {
        for (var offset = 0; offset < data.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, data.Length - offset);
            Assert.IsEmpty(watcher.Render(new ReadOnlySequence<byte>(data, offset, length)));
        }
    }

    [TestMethod]
    public void Render_NeverEmitsText()
    {
        var (watcher, _, _) = Create();

        Assert.IsEmpty(watcher.Render(new ReadOnlySequence<byte>(StreamContentSamples.Png())));
        Assert.IsEmpty(watcher.Render(new ReadOnlySequence<byte>("hello\r\n"u8.ToArray())));
    }

    [TestMethod]
    public void PngSplitAcrossManyReads_WithTextAround_IsCapturedExactlyOnceItEnds()
    {
        var (watcher, _, captures) = Create();
        var png = StreamContentSamples.Png();

        Feed(watcher, [.. "noise before\r\n"u8, .. png, .. "after\r\n"u8], chunkSize: 3);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.Png, captures[0].Kind);
        Assert.AreEqual(StreamCaptureEnd.Complete, captures[0].EndReason);
        Assert.IsFalse(captures[0].WasDeclared);
        CollectionAssert.AreEqual(png, captures[0].Data);
        Assert.IsFalse(watcher.IsCapturing);
    }

    [TestMethod]
    public void SignatureSplitBetweenTwoReads_IsStillFound()
    {
        var (watcher, _, captures) = Create();
        var gif = StreamContentSamples.Gif();

        Feed(watcher, [.. "xx"u8, .. gif.AsSpan(0, 3)]);
        Feed(watcher, gif[3..]);

        Assert.HasCount(1, captures);
        CollectionAssert.AreEqual(gif, captures[0].Data);
    }

    [TestMethod]
    public void TwoImagesInOneRead_AreTwoCaptures()
    {
        var (watcher, _, captures) = Create();

        Feed(watcher, [.. StreamContentSamples.Bmp(), .. StreamContentSamples.Jpeg()]);

        Assert.HasCount(2, captures);
        Assert.AreEqual(StreamContentKind.Bmp, captures[0].Kind);
        Assert.AreEqual(StreamContentKind.Jpeg, captures[1].Kind);
        CollectionAssert.AreEqual(StreamContentSamples.Jpeg(), captures[1].Data);
    }

    [TestMethod]
    public void Hpgl_WithoutAPenStowEnd_EndsOnItsLongerIdleWait()
    {
        var (watcher, time, captures) = Create();
        var plot = "IN;SP1;PU0,0;PD1000,0,1000,1000;"u8.ToArray();

        Feed(watcher, plot, chunkSize: 10);
        time.Advance(TimeSpan.FromSeconds(9.9));
        Assert.IsEmpty(captures, "A pause of a few seconds mid-plot must not split it into two files.");

        time.Advance(TimeSpan.FromSeconds(0.1));

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.Hpgl, captures[0].Kind);
        Assert.AreEqual(StreamCaptureEnd.IdleTimeout, captures[0].EndReason);
        CollectionAssert.AreEqual(plot, captures[0].Data);
    }

    [TestMethod]
    public void Hpgl_EndsAtItsPenStow_AndIgnoresThePromptThatFollows()
    {
        var (watcher, _, captures) = Create();

        Feed(watcher, [.. "IN;SP1;PU0,0;PD5,5;SP0;"u8, .. " READY;\r\n"u8], chunkSize: 4);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamCaptureEnd.Complete, captures[0].EndReason);
        Assert.AreEqual("IN;SP1;PU0,0;PD5,5;SP0;", Encoding.ASCII.GetString(captures[0].Data));
    }

    [TestMethod]
    public void Hpgl_PenStowSplitAcrossReads_StillEndsTheCapture()
    {
        var (watcher, _, captures) = Create();

        Feed(watcher, "IN;SP1;PD5,5;S"u8.ToArray());
        Feed(watcher, "P0;"u8.ToArray());

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamCaptureEnd.Complete, captures[0].EndReason);
    }

    [TestMethod]
    public void MoreDataBeforeTheIdleTimeout_KeepsTheCaptureOpen()
    {
        var (watcher, time, captures) = Create();

        Feed(watcher, "IN;SP1;"u8.ToArray());
        time.Advance(TimeSpan.FromSeconds(6));
        Feed(watcher, "PD10,10;"u8.ToArray());
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.IsEmpty(captures);

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.HasCount(1, captures);
        Assert.AreEqual("IN;SP1;PD10,10;", Encoding.ASCII.GetString(captures[0].Data));
    }

    [TestMethod]
    public void HpglMidLine_IsNotMistakenForAPlot()
    {
        var (watcher, time, captures) = Create();

        Feed(watcher, "status: IN;SP1;PU;\r\n"u8.ToArray());
        time.Advance(_idle * 2);

        Assert.IsEmpty(captures);
    }

    [TestMethod]
    public void HpglAfterAQuietGap_CountsAsANewReply()
    {
        var (watcher, time, captures) = Create();

        Feed(watcher, "status"u8.ToArray());
        time.Advance(_idle);
        Feed(watcher, "IN;SP1;"u8.ToArray());
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.Hpgl, captures[0].Kind);
    }

    [TestMethod]
    public void PlainText_NeverProducesACapture()
    {
        var (watcher, time, captures) = Create();

        Feed(watcher, "+1.23456789E+00\r\nKEITHLEY,MODEL 2000\r\n#Channel 1\r\n"u8.ToArray(), chunkSize: 4);
        time.Advance(_idle * 3);

        Assert.IsEmpty(captures);
    }

    [TestMethod]
    public void UndeclaredScpiBlockWrappingAnImage_CapturesExactlyThePayload()
    {
        var (watcher, _, captures) = Create();
        var png = StreamContentSamples.Png();

        Feed(watcher, [.. StreamContentSamples.ScpiBlock(png), (byte)'\n'], chunkSize: 7);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.Png, captures[0].Kind);
        CollectionAssert.AreEqual(png, captures[0].Data);
    }

    [TestMethod]
    public void DeclaredImage_InABlock_IsCapturedByLengthAndIdentifiedFromItsBytes()
    {
        var (watcher, _, captures) = Create();
        var bmp = StreamContentSamples.Bmp();

        watcher.ExpectResponse(StreamContentFormat.Image);
        Feed(watcher, [.. StreamContentSamples.ScpiBlock(bmp), (byte)'\n'], chunkSize: 1);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.Bmp, captures[0].Kind);
        Assert.IsTrue(captures[0].WasDeclared);
        Assert.AreEqual(StreamCaptureEnd.Complete, captures[0].EndReason);
        CollectionAssert.AreEqual(bmp, captures[0].Data);
    }

    [TestMethod]
    public void DeclaredImage_WithUnrecognizedBytes_IsStillCaptured()
    {
        var (watcher, _, captures) = Create();
        byte[] payload = [1, 2, 3, 4, 5, 6];

        watcher.ExpectResponse(StreamContentFormat.Image);
        Feed(watcher, StreamContentSamples.ScpiBlock(payload));

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.UnknownImage, captures[0].Kind);
        CollectionAssert.AreEqual(payload, captures[0].Data);
    }

    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void DeclaredImage_InAnIndefiniteLengthBlock_IsCapturedByItsOwnStructuralEndWithNoHeaderBytes()
    {
        // Regression test for bug 042: a hinted "#0" indefinite-length block used to be treated as
        // NotABlock, so the "#0" header bytes were left in front of the payload and captured as part
        // of it instead of being stripped. See docs/bugs/resolved/042-stream-watcher-indefinite-block.md.
        var (watcher, _, captures) = Create();
        var png = StreamContentSamples.Png();

        watcher.ExpectResponse(StreamContentFormat.Image);
        Feed(watcher, [.. StreamContentSamples.ScpiIndefiniteBlock(png), (byte)'\n'], chunkSize: 1);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.Png, captures[0].Kind);
        Assert.IsTrue(captures[0].WasDeclared);
        Assert.AreEqual(StreamCaptureEnd.Complete, captures[0].EndReason);
        CollectionAssert.AreEqual(png, captures[0].Data);
    }

    [TestMethod]
    public void DeclaredImage_InAnIndefiniteLengthBlockWithUnrecognizedBytes_EndsOnIdleTimeout()
    {
        var (watcher, time, captures) = Create();
        byte[] payload = [1, 2, 3, 4, 5, 6];

        watcher.ExpectResponse(StreamContentFormat.Image);
        Feed(watcher, StreamContentSamples.ScpiIndefiniteBlock(payload));
        time.Advance(_idle);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.UnknownImage, captures[0].Kind);
        Assert.AreEqual(StreamCaptureEnd.IdleTimeout, captures[0].EndReason);
        CollectionAssert.AreEqual(payload, captures[0].Data);
    }

    [TestMethod]
    public void UndeclaredIndefiniteLengthBlockWrappingAnImage_CapturesExactlyThePayload()
    {
        var (watcher, _, captures) = Create();
        var png = StreamContentSamples.Png();

        Feed(watcher, [.. StreamContentSamples.ScpiIndefiniteBlock(png), (byte)'\n'], chunkSize: 7);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.Png, captures[0].Kind);
        Assert.IsFalse(captures[0].WasDeclared);
        CollectionAssert.AreEqual(png, captures[0].Data);
    }

    [TestMethod]
    public void DeclaredBinary_WithoutABlock_RunsUntilIdle()
    {
        var (watcher, time, captures) = Create();

        watcher.ExpectResponse(StreamContentFormat.Binary);
        Feed(watcher, [0x80, 8, 7]);
        Feed(watcher, [6, 5]);
        time.Advance(_idle);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamContentKind.Binary, captures[0].Kind);
        Assert.AreEqual(StreamCaptureEnd.IdleTimeout, captures[0].EndReason);
        CollectionAssert.AreEqual(new byte[] { 0x80, 8, 7, 6, 5 }, captures[0].Data);
    }

    [TestMethod]
    public void DeclaredText_IsIgnored()
    {
        var (watcher, time, captures) = Create();

        watcher.ExpectResponse(StreamContentFormat.Text);
        Feed(watcher, "+1.0\n"u8.ToArray());
        time.Advance(_idle);

        Assert.IsEmpty(captures);
    }

    [TestMethod]
    public void RunawayStream_IsCutOffAtTheSizeLimit()
    {
        var (watcher, _, captures) = Create(maxCaptureBytes: 64);
        byte[] tiff = [.. StreamContentSamples.Tiff(), .. new byte[200]];

        Feed(watcher, tiff, chunkSize: 16);

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamCaptureEnd.SizeLimit, captures[0].EndReason);
        Assert.HasCount(64, captures[0].Data);
    }

    [TestMethod]
    public void Flush_EndsAnInProgressCapture()
    {
        var (watcher, _, captures) = Create();
        var png = StreamContentSamples.Png();

        Feed(watcher, png[..20]);
        Assert.IsTrue(watcher.IsCapturing);
        watcher.Flush();

        Assert.HasCount(1, captures);
        Assert.AreEqual(StreamCaptureEnd.Flushed, captures[0].EndReason);
        CollectionAssert.AreEqual(png[..20], captures[0].Data);
    }

    [TestMethod]
    public void AThrowingSubscriber_DoesNotEscapeRender()
    {
        var (watcher, _, captures) = Create();
        watcher.ContentDetected += (_, _) => throw new IOException("disk full");

        Feed(watcher, StreamContentSamples.Bmp());

        Assert.HasCount(1, captures);
    }

    [TestMethod]
    public async Task BoundIntoALiveSession_OtherPresentersSeeTextButNotTheCapturedContent()
    {
        var pipe = new Pipe();
        var transport = new Mock<ITransport>();
        transport.SetupGet(t => t.Input).Returns(pipe.Reader);
        transport.SetupGet(t => t.State).Returns(ConnectionState.Open);
        await using var session = new Session(transport.Object, new Pipeline([new RawPresenter()]));

        var watcher = new StreamContentWatcher();
        var captures = new List<StreamCapture>();
        var captured = new TaskCompletionSource();
        watcher.ContentDetected += (_, capture) =>
        {
            captures.Add(capture);
            captured.TrySetResult();
        };
        session.AddPresenter(watcher);

        var outputs = new List<PresenterOutput>();
        var sawOutput = new TaskCompletionSource();
        session.Output += (_, output) =>
        {
            lock (outputs)
            {
                outputs.Add(output);
            }

            sawOutput.TrySetResult();
        };

        await session.OpenAsync(TestContext.CancellationToken);
        await pipe.Writer.WriteAsync("hi"u8.ToArray(), TestContext.CancellationToken);
        await sawOutput.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        var bmp = StreamContentSamples.Bmp();
        await pipe.Writer.WriteAsync(bmp, TestContext.CancellationToken);
        await captured.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        await session.CloseAsync(TestContext.CancellationToken);

        Assert.HasCount(1, captures);
        lock (outputs)
        {
            Assert.HasCount(1, outputs);
            Assert.AreEqual("raw", outputs[0].PresenterName, "only the text before the image reaches other presenters; the image bytes are withheld");
        }

        Assert.IsTrue(session.Presenters.Contains(watcher));

        session.RemovePresenter(watcher);
        Assert.IsFalse(session.Presenters.Contains(watcher));
        watcher.Dispose();
    }
}
