using System.Buffers;
using DevTerm.Core.Presenters;
using Moq;

namespace DevTerm.Core.Tests.Presenters;

[TestClass]
public sealed class PipelineTests
{
    [TestMethod]
    public void Render_FansOutToEveryPresenter()
    {
        var hex = new Mock<IPresenter>();
        hex.SetupGet(p => p.Name).Returns("hex");
        hex.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["48 49"]);

        var ascii = new Mock<IPresenter>();
        ascii.SetupGet(p => p.Name).Returns("ascii");
        ascii.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["HI"]);

        var pipeline = new Pipeline([hex.Object, ascii.Object]);

        var outputs = pipeline.Render(new ReadOnlySequence<byte>(new byte[] { 0x48, 0x49 }));

        CollectionAssert.AreEqual(
            new[] { new PresenterOutput("hex", "48 49"), new PresenterOutput("ascii", "HI") },
            outputs.ToArray());
    }

    [TestMethod]
    public void Render_PresenterReturningNoLines_ProducesNoOutput()
    {
        var buffering = new Mock<IPresenter>();
        buffering.SetupGet(p => p.Name).Returns("buffering");
        buffering.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns([]);

        var pipeline = new Pipeline([buffering.Object]);

        var outputs = pipeline.Render(new ReadOnlySequence<byte>(new byte[] { 0x48 }));

        Assert.IsEmpty(outputs);
    }

    [TestMethod]
    public void Render_PresenterReturningMultipleLines_ProducesOneOutputPerLine()
    {
        var buffering = new Mock<IPresenter>();
        buffering.SetupGet(p => p.Name).Returns("buffering");
        buffering.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["one", "two"]);

        var pipeline = new Pipeline([buffering.Object]);

        var outputs = pipeline.Render(new ReadOnlySequence<byte>(new byte[] { 0x48 }));

        CollectionAssert.AreEqual(
            new[] { new PresenterOutput("buffering", "one"), new PresenterOutput("buffering", "two") },
            outputs.ToArray());
    }

    [TestMethod]
    public void Render_WithNoPresenters_ReturnsEmpty()
    {
        var pipeline = new Pipeline([]);

        var outputs = pipeline.Render(new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 }));

        Assert.IsEmpty(outputs);
    }

    [TestMethod]
    public void Presenters_ExposesConstructorPresentersInOrder()
    {
        var first = new Mock<IPresenter>();
        first.SetupGet(p => p.Name).Returns("first");
        var second = new Mock<IPresenter>();
        second.SetupGet(p => p.Name).Returns("second");

        var pipeline = new Pipeline([first.Object, second.Object]);

        CollectionAssert.AreEqual(new[] { first.Object, second.Object }, pipeline.Presenters.ToArray());
    }
}
