using DevTerm.Test.Utilities;

namespace DevTerm.Transports.Rfc2217.Tests;

[TestCategory(TestCategories.Unit)]
[TestCategory(TestCategories.Rfc2217)]
[TestClass]
public sealed class Rfc2217CodecTests
{
    [TestMethod]
    public void Escape_WithNoIacBytes_ReturnsEquivalentBytes()
    {
        byte[] data = [0x01, 0x02, 0x03];

        var result = Rfc2217Codec.Escape(data);

        Assert.AreSequenceEqual(data, result);
    }

    [TestMethod]
    public void Escape_DoublesEveryIacByte()
    {
        byte[] data = [0x01, Telnet.Iac, 0x02, Telnet.Iac, Telnet.Iac];

        var result = Rfc2217Codec.Escape(data);

        Assert.AreSequenceEqual(new byte[] { 0x01, Telnet.Iac, Telnet.Iac, 0x02, Telnet.Iac, Telnet.Iac, Telnet.Iac, Telnet.Iac }, result);
    }

    [TestMethod]
    public void Unescape_CollapsesDoubledIacPairs()
    {
        byte[] escaped = [0x01, Telnet.Iac, Telnet.Iac, 0x02];

        var result = Rfc2217Codec.Unescape(escaped);

        Assert.AreSequenceEqual(new byte[] { 0x01, Telnet.Iac, 0x02 }, result);
    }

    [TestMethod]
    public void EscapeThenUnescape_RoundTripsArbitraryDataIncludingIac()
    {
        byte[] data = [0x00, 0xFF, 0xFE, 0xFF, 0xFF, 0x7F];

        var roundTripped = Rfc2217Codec.Unescape(Rfc2217Codec.Escape(data));

        Assert.AreSequenceEqual(data, roundTripped);
    }

    [TestMethod]
    public void EncodeSubnegotiation_WrapsCommandAndPayloadWithIacSbSe()
    {
        var result = Rfc2217Codec.EncodeSubnegotiation(Rfc2217Command.SetDataSize, [8]);

        Assert.AreSequenceEqual(
            new byte[] { Telnet.Iac, Telnet.Sb, Telnet.ComPortOption, Rfc2217Command.SetDataSize, 8, Telnet.Iac, Telnet.Se },
            result);
    }

    [TestMethod]
    public void EncodeSubnegotiation_EscapesAnIacByteInThePayload()
    {
        var result = Rfc2217Codec.EncodeSubnegotiation(Rfc2217Command.SetBaudRate, [0xFF, 0x00, 0x00, 0x01]);

        Assert.AreSequenceEqual(
            new byte[] { Telnet.Iac, Telnet.Sb, Telnet.ComPortOption, Rfc2217Command.SetBaudRate, 0xFF, 0xFF, 0x00, 0x00, 0x01, Telnet.Iac, Telnet.Se },
            result);
    }

    [TestMethod]
    public void EncodeSetBaudRate_WritesBigEndianFourByteValue()
    {
        var result = Rfc2217Codec.EncodeSetBaudRate(9600);

        Assert.AreSequenceEqual(
            new byte[] { Telnet.Iac, Telnet.Sb, Telnet.ComPortOption, Rfc2217Command.SetBaudRate, 0x00, 0x00, 0x25, 0x80, Telnet.Iac, Telnet.Se },
            result);
    }

    [TestMethod]
    public void DecodeComPortMessage_EmptyPayload_ReturnsNull()
    {
        Assert.IsNull(Rfc2217Codec.DecodeComPortMessage([]));
    }

    [TestMethod]
    public void DecodeComPortMessage_RecognizedCommandWithTruncatedPayload_ReturnsNull()
    {
        Assert.IsNull(Rfc2217Codec.DecodeComPortMessage([Rfc2217Command.SetBaudRate, 0x00, 0x00]));
    }

    [TestMethod]
    public void DecodeComPortMessage_UnrecognizedCommandCode_ReturnsUnknownMessage()
    {
        var result = Rfc2217Codec.DecodeComPortMessage([99, 0x01, 0x02]);

        var unknown = Assert.IsInstanceOfType<Rfc2217UnknownMessage>(result);
        Assert.AreEqual(99, unknown.CommandCode);
        Assert.AreSequenceEqual(new byte[] { 0x01, 0x02 }, unknown.Payload.ToArray());
        Assert.IsFalse(unknown.IsFromServer);
    }

    [TestMethod]
    public void DecodeComPortMessage_ServerOffsetCommandCode_IsFromServerIsTrue()
    {
        var result = Rfc2217Codec.DecodeComPortMessage([(byte)(Rfc2217Command.SetBaudRate + Rfc2217Command.ServerOffset), 0x00, 0x00, 0x25, 0x80]);

        var message = Assert.IsInstanceOfType<Rfc2217BaudRateMessage>(result);
        Assert.AreEqual(9600, message.BaudRate);
        Assert.IsTrue(message.IsFromServer);
    }

    [TestMethod]
    public void EncodeSetBaudRate_ThenDecode_RoundTrips()
    {
        var encoded = Rfc2217Codec.EncodeSetBaudRate(115200);
        var body = StripFraming(encoded);

        var message = Assert.IsInstanceOfType<Rfc2217BaudRateMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.AreEqual(115200, message.BaudRate);
        Assert.IsFalse(message.IsFromServer);
    }

    [TestMethod]
    public void EncodeSetDataSize_ThenDecode_RoundTrips()
    {
        var body = StripFraming(Rfc2217Codec.EncodeSetDataSize(ComPortDataSize.Seven));

        var message = Assert.IsInstanceOfType<Rfc2217DataSizeMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.AreEqual(ComPortDataSize.Seven, message.DataSize);
    }

    [TestMethod]
    public void EncodeSetParity_ThenDecode_RoundTrips()
    {
        var body = StripFraming(Rfc2217Codec.EncodeSetParity(ComPortParity.Even));

        var message = Assert.IsInstanceOfType<Rfc2217ParityMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.AreEqual(ComPortParity.Even, message.Parity);
    }

    [TestMethod]
    public void EncodeSetStopSize_ThenDecode_RoundTrips()
    {
        var body = StripFraming(Rfc2217Codec.EncodeSetStopSize(ComPortStopSize.OneAndAHalf));

        var message = Assert.IsInstanceOfType<Rfc2217StopSizeMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.AreEqual(ComPortStopSize.OneAndAHalf, message.StopSize);
    }

    [TestMethod]
    public void EncodeSetControl_ThenDecode_RoundTrips()
    {
        var body = StripFraming(Rfc2217Codec.EncodeSetControl(Rfc2217ControlValue.SetDtrStateOn));

        var message = Assert.IsInstanceOfType<Rfc2217ControlMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.AreEqual(Rfc2217ControlValue.SetDtrStateOn, message.Value);
    }

    [TestMethod]
    public void EncodePurgeData_ThenDecode_RoundTrips()
    {
        var body = StripFraming(Rfc2217Codec.EncodePurgeData(Rfc2217PurgeTarget.Both));

        var message = Assert.IsInstanceOfType<Rfc2217PurgeDataMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.AreEqual(Rfc2217PurgeTarget.Both, message.Target);
    }

    [TestMethod]
    public void DecodeComPortMessage_NotifyLineState_DecodesFlags()
    {
        var body = StripFraming(Rfc2217Codec.EncodeSubnegotiation(Rfc2217Command.NotifyLineState, [(byte)(Rfc2217LineState.BreakDetect | Rfc2217LineState.FramingError)]));

        var message = Assert.IsInstanceOfType<Rfc2217NotifyLineStateMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.IsTrue(message.LineState.HasFlag(Rfc2217LineState.BreakDetect));
        Assert.IsTrue(message.LineState.HasFlag(Rfc2217LineState.FramingError));
        Assert.IsTrue(message.IsFromServer);
    }

    [TestMethod]
    public void DecodeComPortMessage_NotifyModemState_DecodesFlags()
    {
        var body = StripFraming(Rfc2217Codec.EncodeSubnegotiation(Rfc2217Command.NotifyModemState, [(byte)(Rfc2217ModemState.ClearToSend | Rfc2217ModemState.RingIndicator)]));

        var message = Assert.IsInstanceOfType<Rfc2217NotifyModemStateMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.IsTrue(message.ModemState.HasFlag(Rfc2217ModemState.ClearToSend));
        Assert.IsTrue(message.ModemState.HasFlag(Rfc2217ModemState.RingIndicator));
        Assert.IsTrue(message.IsFromServer);
    }

    [TestMethod]
    public void EncodeSignatureRequest_ThenDecode_RoundTripsAsciiText()
    {
        var encoded = Rfc2217Codec.EncodeSubnegotiation(Rfc2217Command.Signature, "devterm"u8);
        var body = StripFraming(encoded);

        var message = Assert.IsInstanceOfType<Rfc2217SignatureMessage>(Rfc2217Codec.DecodeComPortMessage(body));
        Assert.AreEqual("devterm", message.Signature);
    }

    /// <summary>Strips the <c>IAC SB COM-PORT-OPTION ... IAC SE</c> framing an <c>Encode*</c> helper adds, leaving the raw command+payload body <see cref="Rfc2217Codec.DecodeComPortMessage"/> expects.</summary>
    private static byte[] StripFraming(byte[] encoded) => encoded[3..^2];
}
