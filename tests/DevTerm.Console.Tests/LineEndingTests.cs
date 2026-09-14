namespace DevTerm.Console.Tests;

[TestClass]
public sealed class LineEndingTests
{
    [TestMethod]
    public void ToBytes_None_ReturnsEmpty() => CollectionAssert.AreEqual(Array.Empty<byte>(), LineEnding.None.ToBytes());

    [TestMethod]
    public void ToBytes_Cr_ReturnsCarriageReturn() => CollectionAssert.AreEqual(new byte[] { 0x0D }, LineEnding.Cr.ToBytes());

    [TestMethod]
    public void ToBytes_Lf_ReturnsLineFeed() => CollectionAssert.AreEqual(new byte[] { 0x0A }, LineEnding.Lf.ToBytes());

    [TestMethod]
    public void ToBytes_CrLf_ReturnsCarriageReturnThenLineFeed() => CollectionAssert.AreEqual(new byte[] { 0x0D, 0x0A }, LineEnding.CrLf.ToBytes());

    [TestMethod]
    public void Append_None_ReturnsPayloadUnchanged()
    {
        var payload = new byte[] { 1, 2, 3 };

        CollectionAssert.AreEqual(payload, LineEnding.None.Append(payload));
    }

    [TestMethod]
    public void Append_Cr_AppendsCarriageReturnAfterPayload()
    {
        var payload = "ID?"u8.ToArray();

        CollectionAssert.AreEqual("ID?\r"u8.ToArray(), LineEnding.Cr.Append(payload));
    }

    [TestMethod]
    public void Append_CrLf_AppendsBothBytesInOrder()
    {
        var payload = new byte[] { 0x41 };

        CollectionAssert.AreEqual(new byte[] { 0x41, 0x0D, 0x0A }, LineEnding.CrLf.Append(payload));
    }

    [TestMethod]
    public void Append_DoesNotMutateTheOriginalPayloadArray()
    {
        var payload = new byte[] { 0x41 };

        _ = LineEnding.Cr.Append(payload);

        CollectionAssert.AreEqual(new byte[] { 0x41 }, payload);
    }
}
