namespace DevTerm.Configuration.Tests;

[TestCategory("UNIT")]
[TestClass]
public sealed class DevTermSessionBuilderTests
{
    [TestMethod]
    public void Build_WithValidOptions_ReturnsAnUnopenedSessionAndTheNamedPresenter()
    {
        var result = DevTermSessionBuilder.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, Presenter = "hex" });

        Assert.IsNotNull(result.Session);
        Assert.AreEqual("hex", result.Presenter.Name);
        Assert.AreEqual(Core.Transports.ConnectionState.Closed, result.Session.State);
    }

    [TestMethod]
    public void Build_WithUnknownPresenter_Throws()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, Presenter = "not-a-real-presenter" };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => DevTermSessionBuilder.Build(options));
        StringAssert.Contains(ex.Message, "not-a-real-presenter");
    }

    [TestMethod]
    public async Task Build_TwiceWithDifferentOptions_ProducesIndependentSessions()
    {
        var first = DevTermSessionBuilder.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 23, Presenter = "ascii" });
        var second = DevTermSessionBuilder.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", TcpPort = 24, Presenter = "hex" });

        Assert.AreNotSame(first.Session, second.Session);
        Assert.AreNotEqual(first.Presenter.Name, second.Presenter.Name);

        await first.Session.DisposeAsync();
        await second.Session.DisposeAsync();
    }
}
