namespace DevTerm.Configuration.Tests;

[TestClass]
[TestCategory("UNIT")]
public sealed class SerialPortOptionTests
{
    [TestMethod]
    public void From_WithADescription_ShowsNameAndDescription()
    {
        var option = SerialPortOption.From("COM3", new Dictionary<string, string> { ["COM3"] = "USB Serial Device" });

        Assert.AreEqual("COM3", option.Name);
        Assert.AreEqual("COM3 — USB Serial Device", option.Display);
    }

    [TestMethod]
    public void From_WithNoDescription_ShowsJustTheName()
    {
        var option = SerialPortOption.From("COM3", new Dictionary<string, string>());

        Assert.AreEqual("COM3", option.Display);
    }

    [TestMethod]
    public void From_WithABlankDescription_ShowsJustTheName()
    {
        var option = SerialPortOption.From("COM3", new Dictionary<string, string> { ["COM3"] = "   " });

        Assert.AreEqual("COM3", option.Display);
    }

    [TestMethod]
    public void From_LooksUpTheDescriptionCaseInsensitively_WhenTheDictionaryIs()
    {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["com3"] = "USB Serial Device" };

        Assert.AreEqual("COM3 — USB Serial Device", SerialPortOption.From("COM3", descriptions).Display);
    }
}
