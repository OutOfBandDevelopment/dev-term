using System.Buffers;
using DevTerm.Core.Presenters;
using Moq;

namespace DevTerm.Core.Tests.Presenters;

[TestCategory("UNIT")]
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

        Assert.AreSequenceEqual(
            new[] { new PresenterOutput("hex", "48 49"), new PresenterOutput("ascii", "HI") }, outputs.ToArray());
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

        Assert.AreSequenceEqual(
            new[] { new PresenterOutput("buffering", "one"), new PresenterOutput("buffering", "two") }, outputs.ToArray());
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

        Assert.AreSequenceEqual(new[] { first.Object, second.Object }, pipeline.Presenters.ToArray());
    }

    [TestMethod]
    public void AddPresenter_AppendsToTheSameLiveInstance()
    {
        var first = new Mock<IPresenter>();
        first.SetupGet(p => p.Name).Returns("first");
        var added = new Mock<IPresenter>();
        added.SetupGet(p => p.Name).Returns("added");

        var pipeline = new Pipeline([first.Object]);
        pipeline.AddPresenter(added.Object);

        Assert.AreSequenceEqual(new[] { first.Object, added.Object }, pipeline.Presenters.ToArray());
    }

    [TestMethod]
    public void AddPresenter_AlreadyPresent_DoesNotDuplicate()
    {
        var presenter = new Mock<IPresenter>();
        presenter.SetupGet(p => p.Name).Returns("scpi");

        var pipeline = new Pipeline([presenter.Object]);
        pipeline.AddPresenter(presenter.Object);

        Assert.AreSequenceEqual(new[] { presenter.Object }, pipeline.Presenters.ToArray());
    }

    [TestMethod]
    public void AddPresenter_AfterConstruction_IsPickedUpByRender()
    {
        var added = new Mock<IPresenter>();
        added.SetupGet(p => p.Name).Returns("added");
        added.Setup(p => p.Render(It.IsAny<ReadOnlySequence<byte>>())).Returns(["hi"]);

        var pipeline = new Pipeline([]);
        pipeline.AddPresenter(added.Object);

        var outputs = pipeline.Render(new ReadOnlySequence<byte>(new byte[] { 0x48 }));

        Assert.AreSequenceEqual(new[] { new PresenterOutput("added", "hi") }, outputs.ToArray());
    }
}
