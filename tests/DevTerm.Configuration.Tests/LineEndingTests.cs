using DevTerm.Test.Utilities;

namespace DevTerm.Configuration.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class LineEndingTests
{
    [TestMethod]
    public void ToBytes_None_ReturnsEmpty() => Assert.AreSequenceEqual([], LineEnding.None.ToBytes());

    [TestMethod]
    public void ToBytes_Cr_ReturnsCarriageReturn() => Assert.AreSequenceEqual("\r"u8.ToArray(), LineEnding.Cr.ToBytes());

    [TestMethod]
    public void ToBytes_Lf_ReturnsLineFeed() => Assert.AreSequenceEqual("\n"u8.ToArray(), LineEnding.Lf.ToBytes());

    [TestMethod]
    public void ToBytes_CrLf_ReturnsCarriageReturnThenLineFeed() => Assert.AreSequenceEqual("\r\n"u8.ToArray(), LineEnding.CrLf.ToBytes());

    [TestMethod]
    public void Append_None_ReturnsPayloadUnchanged()
    {
        var payload = new byte[] { 1, 2, 3 };

        Assert.AreSequenceEqual(payload, LineEnding.None.Append(payload));
    }

    [TestMethod]
    public void Append_Cr_AppendsCarriageReturnAfterPayload()
    {
        var payload = "ID?"u8.ToArray();

        Assert.AreSequenceEqual("ID?\r"u8.ToArray(), LineEnding.Cr.Append(payload));
    }

    [TestMethod]
    public void Append_CrLf_AppendsBothBytesInOrder()
    {
        var payload = "A"u8.ToArray();

        Assert.AreSequenceEqual("A\r\n"u8.ToArray(), LineEnding.CrLf.Append(payload));
    }

    [TestMethod]
    public void Append_DoesNotMutateTheOriginalPayloadArray()
    {
        var payload = "A"u8.ToArray();

        _ = LineEnding.Cr.Append(payload);

        Assert.AreSequenceEqual("A"u8.ToArray(), payload);
    }
}
