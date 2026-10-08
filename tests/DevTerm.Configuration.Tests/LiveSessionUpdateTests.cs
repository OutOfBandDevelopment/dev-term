using System.IO.Pipelines;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class LiveSessionUpdateTests
{
    private static CliOptions Serial() => new() { Transport = "serial", Port = "COM9", Baud = 9600, Presenter = ["ascii"] };

    [TestMethod]
    public void CanApplyLive_IsTrueForLineSettingsPresentersParserAndLineEnding()
    {
        var current = Serial();
        var next = Serial();
        next.Baud = 115200;
        next.DataBits = 7;
        next.Dtr = false;
        next.Presenter = ["hex", "ascii"];
        next.Parser = "hex";

        Assert.IsTrue(LiveSessionUpdate.CanApplyLive(current, next));
    }

    [TestMethod]
    public void CanApplyLive_IsFalseWhenTheConnectionItselfChanges()
    {
        var other = Serial();
        other.Port = "COM10";
        Assert.IsFalse(LiveSessionUpdate.CanApplyLive(Serial(), other));

        var tcp = Serial();
        tcp.Transport = "tcp";
        Assert.IsFalse(LiveSessionUpdate.CanApplyLive(Serial(), tcp));
    }

    [TestMethod]
    public void TryApply_AppliesLineSettingsAndSwapsPresenters_WithoutReopening()
    {
        var catalog = DevTermSessionBuilder.Build(Serial()).Catalog;
        var current = Serial();
        var transport = new Mock<ITransport>();
        var control = transport.As<IComPortControl>();
        control.Setup(c => c.SetBaudRate(It.IsAny<int>())).Returns(control.Object);
        var session = new Session(transport.Object, new Pipeline(DevTermSessionBuilder.ResolvePresenters(catalog, current)));

        var next = Serial();
        next.Baud = 19200;
        next.Presenter = ["hex"];

        Assert.IsTrue(LiveSessionUpdate.TryApply(session, catalog, current, next));
        control.Verify(c => c.SetBaudRate(19200), Times.Once);
        control.Verify(c => c.SetDataBits(It.IsAny<int>()), Times.Never);
        CollectionAssert.AreEqual(new[] { "hex" }, session.Presenters.Select(p => p.Name).ToArray());
        transport.Verify(t => t.OpenAsync(It.IsAny<CancellationToken>()), Times.Never);
        transport.Verify(t => t.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void TryApply_ReturnsFalse_ForAnUnknownPresenter()
    {
        var catalog = DevTermSessionBuilder.Build(Serial()).Catalog;
        var current = Serial();
        var session = new Session(Mock.Of<ITransport>(), new Pipeline(DevTermSessionBuilder.ResolvePresenters(catalog, current)));
        var next = Serial();
        next.Presenter = ["nonesuch"];

        Assert.IsFalse(LiveSessionUpdate.TryApply(session, catalog, current, next));
    }
}
