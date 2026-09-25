using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class DevTermSessionBuilderTests
{
    [TestMethod]
    public void Build_WithValidOptions_ReturnsAnUnopenedSessionAndTheNamedPresenter()
    {
        var result = DevTermSessionBuilder.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["hex"] });

        Assert.IsNotNull(result.Session);
        Assert.AreSequenceEqual(["hex"], [.. result.Session.Presenters.Select(p => p.Name)]);
        Assert.AreEqual(Core.Transports.ConnectionState.Closed, result.Session.State);
    }

    [TestMethod]
    public void Build_WithUnknownPresenter_Throws()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["not-a-real-presenter"] };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => DevTermSessionBuilder.Build(options));
        Assert.Contains("not-a-real-presenter", ex.Message);
    }

    [TestMethod]
    public void Build_WithSeveralPresenters_FansOutToAllOfThemInOrder()
    {
        var result = DevTermSessionBuilder.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii", "hex", "ASCII"] });

        Assert.AreSequenceEqual(["ascii", "hex"], [.. result.Session.Presenters.Select(p => p.Name)], "Duplicates (any case) collapse.");
    }

    [TestMethod]
    public void Build_WithAParserThatIsNotARegisteredPresenter_Throws()
    {
        var options = new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Parser = "nope" };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => DevTermSessionBuilder.Build(options));
        Assert.Contains("nope", ex.Message);
    }

    [TestMethod]
    public void Build_ParserDefaultsToTheFirstPresenter_AndIsResolvableFromTheCatalog()
    {
        var result = DevTermSessionBuilder.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["decimal", "hex"] });

        Assert.IsTrue(result.Catalog.TryGetInput("decimal", out _));
        Assert.AreSequenceEqual(["ascii", "utf8", "hex", "decimal", "octal", "binary"], [.. result.Catalog.InputNames], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task Build_TwiceWithDifferentOptions_ProducesIndependentSessions()
    {
        var first = DevTermSessionBuilder.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "23", Presenter = ["ascii"] });
        var second = DevTermSessionBuilder.Build(new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "24", Presenter = ["hex"] });

        Assert.AreNotSame(first.Session, second.Session);
        Assert.AreNotEqual(first.Session.Presenters[0].Name, second.Session.Presenters[0].Name);

        await first.Session.DisposeAsync();
        await second.Session.DisposeAsync();
    }
}
