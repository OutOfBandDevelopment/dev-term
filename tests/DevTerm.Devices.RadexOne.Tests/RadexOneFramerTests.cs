using DevTerm.Test.Utilities;

namespace DevTerm.Devices.RadexOne.Tests;

/// <summary>
/// Verifies <see cref="RadexOneFramer"/>'s build/parse round-trip, its checksum/prefix rejection
/// paths, and (via <see cref="BuildRequest_ReadDataQuery_MatchesTheSourceDocsRealHardwareTrace"/>)
/// its exact byte-for-byte agreement with a real captured request/reply pair from the source
/// reverse-engineering doc — pure byte-layout logic, no transport involved, so this is <c>UNIT</c>.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Hid)]
[TestCategory(TestCategories.Radex_One)]
[TestClass]
public sealed class RadexOneFramerTests
{
    [TestMethod]
    public void BuildReply_ThenTryParseReply_RoundTripsPacketNumberAndExtension()
    {
        byte[] extension = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        var packet = RadexOneFramer.BuildReply(42, extension);

        var parsed = RadexOneFramer.TryParseReply(packet, out var packetNumber, out var parsedExtension);

        Assert.IsTrue(parsed);
        Assert.AreEqual((ushort)42, packetNumber);
        CollectionAssert.AreEqual(extension, parsedExtension);
    }

    [TestMethod]
    public void BuildReply_WithEmptyExtension_RoundTrips()
    {
        var packet = RadexOneFramer.BuildReply(1, []);

        var parsed = RadexOneFramer.TryParseReply(packet, out _, out var extension);

        Assert.IsTrue(parsed);
        Assert.IsEmpty(extension);
    }

    [TestMethod]
    public void TryParseReply_WithTrailingPadding_IgnoresBytesPastDeclaredExtensionLength()
    {
        var packet = RadexOneFramer.BuildReply(1, [0xAA, 0xBB, 0xCC]);
        var padded = new byte[64];
        packet.CopyTo(padded, 0);

        var parsed = RadexOneFramer.TryParseReply(padded, out _, out var extension);

        Assert.IsTrue(parsed);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC }, extension);
    }

    [TestMethod]
    public void TryParseReply_WithOutboundPrefix_Fails()
    {
        var packet = RadexOneFramer.BuildRequest(1, []);

        var parsed = RadexOneFramer.TryParseReply(packet, out _, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryParseReply_WithCorruptedChecksum_Fails()
    {
        var packet = RadexOneFramer.BuildReply(1, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        packet[10] ^= 0xFF;

        var parsed = RadexOneFramer.TryParseReply(packet, out _, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryParseReply_WithTooShortBuffer_Fails()
    {
        byte[] tooShort = [0x7A, 0xFF, 0x00];

        var parsed = RadexOneFramer.TryParseReply(tooShort, out _, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryParseReply_WithTruncatedExtension_Fails()
    {
        var packet = RadexOneFramer.BuildReply(1, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        var truncated = packet[..^2];

        var parsed = RadexOneFramer.TryParseReply(truncated, out _, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void BuildRequest_ReadDataQuery_MatchesTheSourceDocsRealHardwareTrace()
    {
        // docs/design/proposals/radex-one-protocol.md's source trace:
        // >: 7BFF 2000 0600 1800 0000 4600 0008 0C00 F3F7
        byte[] expected =
        [
            0x7B, 0xFF, 0x20, 0x00, 0x06, 0x00, 0x18, 0x00, 0x00, 0x00, 0x46, 0x00,
            0x00, 0x08, 0x0C, 0x00, 0xF3, 0xF7,
        ];

        var packet = RadexOneFramer.BuildRequest(0x0018, RadexOneExtensionCodec.BuildQuery(RadexOneCommand.ReadData));

        CollectionAssert.AreEqual(expected, packet);
    }

    [TestMethod]
    public void TryParseReply_ReadDataResponse_MatchesTheSourceDocsRealHardwareTrace()
    {
        // docs/design/proposals/radex-one-protocol.md's source trace:
        // <: 7AFF 2080 1600 1800 0000 3680 0008 0000 0C00 0000 1200 0000 1200 0000 1500 0000 BAF7
        // (checksum here only matches once it's a word-sum wrapped mod 0xFFFF — a byte-sum never
        // reproduces 0x8036, since the outer header's word-sum exceeds 0xFFFF on this exact trace.)
        byte[] reply =
        [
            0x7A, 0xFF, 0x20, 0x80, 0x16, 0x00, 0x18, 0x00, 0x00, 0x00, 0x36, 0x80,
            0x00, 0x08, 0x00, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x12, 0x00, 0x00, 0x00,
            0x12, 0x00, 0x00, 0x00, 0x15, 0x00, 0x00, 0x00, 0xBA, 0xF7,
        ];

        var parsed = RadexOneFramer.TryParseReply(reply, out var packetNumber, out var extension);

        Assert.IsTrue(parsed);
        Assert.AreEqual((ushort)0x0018, packetNumber);
        Assert.IsTrue(RadexOneExtensionCodec.TryParseReadData(extension, out var ambient, out var accumulated, out var cpm));
        Assert.AreEqual((ushort)0x12, ambient);
        Assert.AreEqual((ushort)0x12, accumulated);
        Assert.AreEqual((ushort)0x15, cpm);
    }
}
