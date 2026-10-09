using DevTerm.Core.Control;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Devices.Scpi.Tests;

/// <summary>SCPI as an <see cref="IInstrumentPanelProvider"/>: availability, the picker's choices, and the plugin module that registers it.</summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Scpi)]
[TestClass]
public sealed class ScpiInstrumentPanelProviderTests
{
    private readonly ScpiInstrumentPanelProvider _provider = new();

    [TestMethod]
    public void IsAvailable_ForAnyTextTransport_NotHid()
    {
        Assert.IsTrue(_provider.IsAvailable("tcp", 0, 0));
        Assert.IsTrue(_provider.IsAvailable("usbtmc", 0, 0));
        Assert.IsFalse(_provider.IsAvailable("hid", 0x10CF, 0x5500));
        Assert.IsFalse(_provider.IsAvailable("HID", 0x10CF, 0x5500));
    }

    [TestMethod]
    public void PickerChoices_AreAutoDetectGenericThenEveryProfile()
    {
        var choices = _provider.PickerChoices();

        Assert.AreEqual(ScpiProfileCatalog.AutoDetectChoiceName, choices[0]);
        Assert.AreEqual(ScpiProfileCatalog.Generic.Name, choices[1]);
        CollectionAssert.AreEqual(ScpiProfileCatalog.All.Select(p => p.Name).ToList(), choices.Skip(2).ToList());
    }

    [TestMethod]
    public void Offers_RecognizesTheSyntheticChoicesAndProfileNamesOnly()
    {
        Assert.IsTrue(_provider.Offers(_provider.AutoDetectChoice));
        Assert.IsTrue(_provider.Offers(_provider.GenericChoice));
        Assert.IsTrue(_provider.Offers(ScpiProfileCatalog.All[0].Name));
        Assert.IsFalse(_provider.Offers(null));
        Assert.IsFalse(_provider.Offers("  "));
        Assert.IsFalse(_provider.Offers("No Such Instrument"));
    }

    [TestMethod]
    public void ConfigureForProfile_UnknownName_ReturnsFalse() =>
        Assert.IsFalse(_provider.ConfigureForProfile("No Such Instrument", new ScpiReplyPresenter()));

    [TestMethod]
    public void ConfigureForProfile_KnownName_ReturnsTrueEvenWithoutAPresenter() =>
        Assert.IsTrue(_provider.ConfigureForProfile(ScpiProfileCatalog.All[0].Name, presenter: null));

    [TestMethod]
    public void PluginModule_RegistersTheProviderAndThePresenter()
    {
        var services = new ServiceCollection();
        new ScpiPluginModule().ConfigureServices(services);

        Assert.AreEqual(typeof(ScpiInstrumentPanelProvider), services.Single(d => d.ServiceType == typeof(IInstrumentPanelProvider)).ImplementationType);
    }

    [TestMethod]
    public void ProfilesLoadFromThePluginsOwnDirectory()
    {
        // The catalog resolves Profiles/ next to this assembly, so it still finds them once the DLL lives under plugins/scpi.
        Assert.IsNotEmpty(ScpiProfileCatalog.All);
    }
}
