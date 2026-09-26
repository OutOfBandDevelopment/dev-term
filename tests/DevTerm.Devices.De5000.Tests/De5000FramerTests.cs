using DevTerm.Test.Utilities;

namespace DevTerm.Devices.De5000.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class De5000FramerTests
{
    // header 00 0D | flags 00 | freq/tol 40 00 | primary 01 04 D2 33 00 | secondary 01 00 0C 03 00 | footer 0D 0A
    // primary: Ls (series, code 1), raw 0x04D2=1234, unit mH (6), multiplier 3 -> 1.234 mH, status normal.
    // secondary: D (code 1), raw 0x000C=12, unit "" (0), multiplier 3 -> 0.012, status normal.
    // frequency: code 2 (bits 5-7 of 0x40) -> "1 kHz".
    private static readonly byte[] _validFrame =
    [
        0x00, 0x0D,
        0x00,
        0x40,
        0x00,
        0x01, 0x04, 0xD2, 0x33, 0x00,
        0x01, 0x00, 0x0C, 0x03, 0x00,
        0x0D, 0x0A,
    ];

    [TestMethod]
    public void TryParse_ValidFrame_DecodesAllFields()
    {
        var result = De5000Framer.TryParse(_validFrame, out var frame);

        Assert.IsTrue(result);
        Assert.AreEqual("Ls", frame.PrimaryQuantity);
        Assert.AreEqual(1.234, frame.PrimaryValue, 1e-9);
        Assert.AreEqual("mH", frame.PrimaryUnit);
        Assert.AreEqual("normal", frame.PrimaryStatus);
        Assert.AreEqual("D", frame.SecondaryQuantity);
        Assert.AreEqual(0.012, frame.SecondaryValue, 1e-9);
        Assert.AreEqual(string.Empty, frame.SecondaryUnit);
        Assert.AreEqual("normal", frame.SecondaryStatus);
        Assert.AreEqual("1 kHz", frame.Frequency);
        Assert.IsFalse(frame.Hold);
        Assert.IsFalse(frame.Parallel);
    }

    [TestMethod]
    public void TryParse_WrongLength_ReturnsFalse()
    {
        var result = De5000Framer.TryParse(_validFrame.AsSpan()[..16], out _);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParse_WrongHeader_ReturnsFalse()
    {
        var bad = (byte[])_validFrame.Clone();
        bad[0] = 0xFF;

        var result = De5000Framer.TryParse(bad, out _);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParse_WrongFooter_ReturnsFalse()
    {
        var bad = (byte[])_validFrame.Clone();
        bad[16] = 0x0D; // The real footer is 0D 0A, not 0D 0D.

        var result = De5000Framer.TryParse(bad, out _);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParse_AllModeFlagsSet_SetsEveryFlagProperty()
    {
        var bad = (byte[])_validFrame.Clone();
        bad[2] = 0xFF;

        var result = De5000Framer.TryParse(bad, out var frame);

        Assert.IsTrue(result);
        Assert.IsTrue(frame.Hold);
        Assert.IsTrue(frame.ReferenceShown);
        Assert.IsTrue(frame.Delta);
        Assert.IsTrue(frame.Calibration);
        Assert.IsTrue(frame.Sorting);
        Assert.IsTrue(frame.LcrAuto);
        Assert.IsTrue(frame.AutoRange);
        Assert.IsTrue(frame.Parallel);
    }

    [TestMethod]
    public void TryParse_ParallelFlagSet_UsesTheParallelQuantityTable()
    {
        var bad = (byte[])_validFrame.Clone();
        bad[2] = 0b1000_0000; // Parallel flag only.

        var result = De5000Framer.TryParse(bad, out var frame);

        Assert.IsTrue(result);
        Assert.AreEqual("Lp", frame.PrimaryQuantity);
    }

    [TestMethod]
    public void TryParse_UnknownPrimaryQuantityCode_ReturnsNullQuantity()
    {
        var bad = (byte[])_validFrame.Clone();
        bad[5] = 0xFF;

        var result = De5000Framer.TryParse(bad, out var frame);

        Assert.IsTrue(result);
        Assert.IsNull(frame.PrimaryQuantity);
    }

    [TestMethod]
    public void TryParse_SecondaryStatusMask_NeverReachesTheThreeUnreachableCodes()
    {
        // Secondary status (byte 14) is masked to 3 bits in the real protocol, unlike primary's 4
        // bits - so FAIL/OPEn/Srt (indices 8-10) can never be produced for the secondary reading.
        var bad = (byte[])_validFrame.Clone();
        bad[14] = 0xFF;

        var result = De5000Framer.TryParse(bad, out var frame);

        Assert.IsTrue(result);
        Assert.AreEqual("PASS", frame.SecondaryStatus);
    }

    [TestMethod]
    public void TryParse_UnusedFrequencyCode_ReturnsNull()
    {
        var bad = (byte[])_validFrame.Clone();
        bad[3] = 0b1100_0000; // Code 6 - only 0-5 are defined.

        var result = De5000Framer.TryParse(bad, out var frame);

        Assert.IsTrue(result);
        Assert.IsNull(frame.Frequency);
    }
}
