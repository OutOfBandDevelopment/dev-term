using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class LastPickedColorsTests
{
    [TestMethod]
    public void ToHex_IsHashRrggbb() => Assert.AreEqual("#0A0BFF", LastPickedColors.ToHex((0x0A, 0x0B, 0xFF)));

    [TestMethod]
    [DataRow((byte)255, (byte)255, (byte)0, true)]
    [DataRow((byte)255, (byte)255, (byte)255, true)]
    [DataRow((byte)0, (byte)0, (byte)128, false)]
    [DataRow((byte)0, (byte)0, (byte)0, false)]
    [DataRow((byte)0xFF, (byte)0x66, (byte)0x00, true)] // orange: black 7.2:1, white 2.9:1
    [DataRow((byte)0xFF, (byte)0x00, (byte)0x00, true)] // red: black 5.3:1, white 4.0:1
    [DataRow((byte)0x80, (byte)0x00, (byte)0x80, false)] // purple
    public void UseDarkText_PicksTheReadableTextColor(byte r, byte g, byte b, bool dark) =>
        Assert.AreEqual(dark, LastPickedColors.UseDarkText((r, g, b)));

    [TestMethod]
    public void TryGet_FalseUntilSet()
    {
        var id = $"custom-{Guid.NewGuid():N}";
        Assert.IsFalse(LastPickedColors.TryGet(id, out _));

        LastPickedColors.Set(id, (1, 2, 3));

        Assert.IsTrue(LastPickedColors.TryGet(id, out var color));
        Assert.AreEqual(((byte)1, (byte)2, (byte)3), color);
    }
}
