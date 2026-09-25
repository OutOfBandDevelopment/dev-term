using DevTerm.Core.Presenters;
using DevTerm.Test.Utilities;
using Moq;

namespace DevTerm.Core.Tests.Presenters;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class PresenterCatalogTests
{
    [TestMethod]
    public void Get_ReturnsPresenterByName_CaseInsensitive()
    {
        var hex = new Mock<IPresenter>();
        hex.SetupGet(p => p.Name).Returns("hex");

        var catalog = new PresenterCatalog([hex.Object]);

        Assert.AreSame(hex.Object, catalog.Get("HEX"));
    }

    [TestMethod]
    public void Get_UnknownName_ThrowsKeyNotFoundException()
    {
        var catalog = new PresenterCatalog([]);

        Assert.ThrowsExactly<KeyNotFoundException>(() => catalog.Get("nope"));
    }

    [TestMethod]
    public void TryGet_UnknownName_ReturnsFalse()
    {
        var catalog = new PresenterCatalog([]);

        var found = catalog.TryGet("nope", out var presenter);

        Assert.IsFalse(found);
        Assert.IsNull(presenter);
    }

    [TestMethod]
    public void Names_ReflectsRegisteredPresenters()
    {
        var hex = new Mock<IPresenter>();
        hex.SetupGet(p => p.Name).Returns("hex");
        var ascii = new Mock<IPresenter>();
        ascii.SetupGet(p => p.Name).Returns("ascii");

        var catalog = new PresenterCatalog([hex.Object, ascii.Object]);

        Assert.AreSequenceEqual(["hex", "ascii"], [.. catalog.Names], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }
}
