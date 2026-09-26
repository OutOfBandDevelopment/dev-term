using DevTerm.Test.Utilities;

namespace DevTerm.Devices.RadexOne.Tests;

/// <summary>
/// Verifies <see cref="RadexOneFramer"/>'s build/parse round-trip and its checksum/prefix rejection
/// paths — pure byte-layout logic, no transport involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Hid)]
[TestCategory(TestCategories.Radex_One)]
[TestClass]
public sealed class RadexOneFramerTests
{
    [TestMethod]
    public void BuildReply_ThenTryParseReply_RoundTripsTypeNumberAndExtension()
    {
        byte[] extension = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        var packet = RadexOneFramer.BuildReply(RadexOneCommand.ReadData, 42, extension);

        var parsed = RadexOneFramer.TryParseReply(packet, out var type, out var packetNumber, out var parsedExtension);

        Assert.IsTrue(parsed);
        Assert.AreEqual(RadexOneCommand.ReadData, type);
        Assert.AreEqual((ushort)42, packetNumber);
        CollectionAssert.AreEqual(extension, parsedExtension);
    }

    [TestMethod]
    public void BuildReply_WithEmptyExtension_RoundTrips()
    {
        var packet = RadexOneFramer.BuildReply(RadexOneCommand.ReadSerialVersion, 1, []);

        var parsed = RadexOneFramer.TryParseReply(packet, out var type, out _, out var extension);

        Assert.IsTrue(parsed);
        Assert.AreEqual(RadexOneCommand.ReadSerialVersion, type);
        Assert.IsEmpty(extension);
    }

    [TestMethod]
    public void TryParseReply_WithTrailingPadding_IgnoresBytesPastDeclaredExtensionLength()
    {
        var packet = RadexOneFramer.BuildReply(RadexOneCommand.ReadSettings, 1, [0xAA, 0xBB, 0xCC]);
        var padded = new byte[64];
        packet.CopyTo(padded, 0);

        var parsed = RadexOneFramer.TryParseReply(padded, out _, out _, out var extension);

        Assert.IsTrue(parsed);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC }, extension);
    }

    [TestMethod]
    public void TryParseReply_WithOutboundPrefix_Fails()
    {
        var packet = RadexOneFramer.BuildRequest(RadexOneCommand.ReadData, 1, []);

        var parsed = RadexOneFramer.TryParseReply(packet, out _, out _, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryParseReply_WithCorruptedChecksum_Fails()
    {
        var packet = RadexOneFramer.BuildReply(RadexOneCommand.ReadData, 1, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        packet[10] ^= 0xFF;

        var parsed = RadexOneFramer.TryParseReply(packet, out _, out _, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryParseReply_WithTooShortBuffer_Fails()
    {
        byte[] tooShort = [0x7A, 0xFF, 0x00];

        var parsed = RadexOneFramer.TryParseReply(tooShort, out _, out _, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryParseReply_WithTruncatedExtension_Fails()
    {
        var packet = RadexOneFramer.BuildReply(RadexOneCommand.ReadData, 1, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        var truncated = packet[..^2];

        var parsed = RadexOneFramer.TryParseReply(truncated, out _, out _, out _);

        Assert.IsFalse(parsed);
    }
}
