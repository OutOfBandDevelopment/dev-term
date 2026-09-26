using System.Text;
using DevTerm.Core.StreamContent;
using DevTerm.Test.Utilities;

namespace DevTerm.Core.Tests.StreamContent;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class StreamContentEndFinderTests
{
    public static IEnumerable<object[]> StructuredSamples =>
    [
        [StreamContentSamples.Png(), StreamContentKind.Png],
        [StreamContentSamples.Jpeg(), StreamContentKind.Jpeg],
        [StreamContentSamples.Gif(), StreamContentKind.Gif],
        [StreamContentSamples.Bmp(), StreamContentKind.Bmp],
        [StreamContentSamples.PostScript(), StreamContentKind.PostScript],
        [StreamContentSamples.PjlPcl(), StreamContentKind.Pcl],
    ];

    [TestMethod]
    [DynamicData(nameof(StructuredSamples))]
    public void FindEnd_WholeSampleFollowedByMore_EndsExactlyAtTheSamplesEnd(byte[] sample, StreamContentKind kind)
    {
        var finder = StreamContentEndFinder.For(kind)!;
        byte[] withTrailer = [.. sample, .. "trailing text\r\n"u8];

        Assert.AreEqual((long)sample.Length, finder.FindEnd(withTrailer));
    }

    [TestMethod]
    [DynamicData(nameof(StructuredSamples))]
    public void FindEnd_FedOneByteAtATime_EndsOnlyOnceTheLastByteArrives(byte[] sample, StreamContentKind kind)
    {
        var finder = StreamContentEndFinder.For(kind)!;

        for (var length = 1; length < sample.Length; length++)
        {
            Assert.IsNull(finder.FindEnd(sample.AsSpan(0, length)), $"ended early at {length} of {sample.Length} bytes");
        }

        Assert.AreEqual((long)sample.Length, finder.FindEnd(sample));
    }

    [TestMethod]
    public void FindEnd_PostScriptEndedByCtrlD_EndsAfterIt()
    {
        var finder = StreamContentEndFinder.For(StreamContentKind.PostScript)!;
        var job = Encoding.ASCII.GetBytes("%!PS\nshowpage\n\u0004more");

        Assert.AreEqual(15L, finder.FindEnd(job));
    }

    [TestMethod]
    public void FindEnd_BinaryEps_EndsWhereItsLastSectionDoes()
    {
        var eps = new byte[64];
        eps[0] = 0xC5;
        eps[1] = 0xD0;
        eps[2] = 0xD3;
        eps[3] = 0xC6;
        eps[4] = 30; // PostScript section at 30, 20 bytes long
        eps[8] = 20;
        eps[20] = 50; // TIFF preview at 50, 10 bytes long
        eps[24] = 10;

        Assert.AreEqual(60L, StreamContentEndFinder.For(StreamContentKind.PostScript)!.FindEnd(eps));
    }

    [TestMethod]
    public void FindEnd_PclStartingWithABareReset_NeverEnds()
    {
        var finder = StreamContentEndFinder.For(StreamContentKind.Pcl)!;

        Assert.IsNull(finder.FindEnd("\u001bE\u001b&l0Ohello\u001bE"u8));
    }

    [TestMethod]
    public void FindEnd_JpegWithACorruptSegment_GivesUpRatherThanGuessing()
    {
        byte[] corrupt = [0xFF, 0xD8, 0x12, 0x34, 0xFF, 0xD9];

        Assert.IsNull(StreamContentEndFinder.For(StreamContentKind.Jpeg)!.FindEnd(corrupt));
    }

    [TestMethod]
    public void For_FormatsWithNoInBandEnd_HaveNoFinder()
    {
        Assert.IsNull(StreamContentEndFinder.For(StreamContentKind.Hpgl));
        Assert.IsNull(StreamContentEndFinder.For(StreamContentKind.Tiff));
        Assert.IsNull(StreamContentEndFinder.For(StreamContentKind.Binary));
        Assert.IsNull(StreamContentEndFinder.For(StreamContentKind.UnknownImage));
    }

    [TestMethod]
    public void FixedLength_EndsAtTheDeclaredLength()
    {
        var finder = StreamContentEndFinder.FixedLength(4);

        Assert.IsNull(finder.FindEnd([1, 2, 3]));
        Assert.AreEqual(4L, finder.FindEnd([1, 2, 3, 4, 5]));
    }
}
