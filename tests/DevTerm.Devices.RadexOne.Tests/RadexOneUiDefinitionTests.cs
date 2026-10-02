using DevTerm.Test.Utilities;

namespace DevTerm.Devices.RadexOne.Tests;

/// <summary>
/// The Radex One panel's static <see cref="UiDefinitions.UiDefinition"/> metadata — no live device or
/// session involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class RadexOneUiDefinitionTests
{
    [TestMethod]
    [TestCategory(TestCategories.BugRegression)]
    public void Build_DescriptionDoesNotClaimUsbHid()
    {
        // Regression test for bug 047: the panel's description said "USB HID geiger counter", but the
        // Radex One actually talks over a serial transport (see docs/bugs/resolved/061-radexone-wrong-baud-rate.md
        // and RadexOneFramer) — not USB HID. See docs/bugs/resolved/047-radexone-description-says-hid.md.
        var definition = RadexOneUiDefinition.Build();

        Assert.IsFalse(definition.Description!.Contains("USB HID", StringComparison.Ordinal), $"Description still claims USB HID: \"{definition.Description}\"");
    }
}
