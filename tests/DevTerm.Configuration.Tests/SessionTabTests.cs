using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class SessionTabTests
{
    [TestMethod]
    public void Build_WithValidOptions_ReturnsATabHoldingTheBuiltSessionAndCatalog()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["hex"] };

        var tab = SessionTab.Build(options);

        Assert.IsNotNull(tab.Session);
        Assert.IsNotNull(tab.Catalog);
        Assert.AreSame(options, tab.CliOptions);
        Assert.IsNotNull(tab.Services);
        Assert.AreEqual(Core.Transports.ConnectionState.Closed, tab.Session.State);
    }

    [TestMethod]
    public void Build_WithUnknownPresenter_Throws()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["not-a-real-presenter"] };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => SessionTab.Build(options));
        Assert.Contains("not-a-real-presenter", ex.Message);
    }

    [TestMethod]
    public void Constructor_ParserDefaultsToTheOptionsEffectiveParser()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["decimal", "hex"] };

        var tab = SessionTab.Build(options);

        Assert.AreEqual("decimal", tab.Parser);
    }

    [TestMethod]
    public void Constructor_WithNoServices_LeavesServicesNull()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] };
        var built = DevTermSessionBuilder.Build(options);

        var tab = new SessionTab(built.Session, built.Catalog, options);

        Assert.IsNull(tab.Services);
    }

    [TestMethod]
    public void Constructor_WithNullSession_Throws()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] };
        var built = DevTermSessionBuilder.Build(options);

        Assert.ThrowsExactly<ArgumentNullException>(() => new SessionTab(null!, built.Catalog, options));
    }

    [TestMethod]
    public void HasItsOwnSendHistory_IndependentOfOtherTabs()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] };

        var first = SessionTab.Build(options);
        var second = SessionTab.Build(options);
        first.SendHistory.Add("hello");

        Assert.AreNotSame(first.SendHistory, second.SendHistory);
        Assert.AreSequenceEqual(["hello"], [.. first.SendHistory.Items]);
        Assert.AreSequenceEqual([], [.. second.SendHistory.Items]);
    }

    [TestMethod]
    public void Title_ForANonSavedConnection_MatchesConnectionDescriptionWindowTitle()
    {
        var store = new ConnectionProfileStore(Path.Combine(Path.GetTempPath(), $"devterm-tests-{Guid.NewGuid():N}"));
        var options = new CliOptions { Transport = "tcp", Host = "192.168.0.110", Port = "23", Presenter = ["ascii", "hex"] };
        var tab = SessionTab.Build(options);

        Assert.AreEqual(ConnectionDescription.WindowTitle(options, tab.Parser, store, false), tab.Title(store));
    }

    [TestMethod]
    public async Task Build_TwiceWithDifferentOptions_ProducesIndependentTabs()
    {
        var first = SessionTab.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] });
        var second = SessionTab.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "24", Presenter = ["hex"] });

        Assert.AreNotSame(first.Session, second.Session);
        Assert.AreNotEqual(first.Session.Presenters[0].Name, second.Session.Presenters[0].Name);

        await first.Session.DisposeAsync();
        await second.Session.DisposeAsync();
    }
}
