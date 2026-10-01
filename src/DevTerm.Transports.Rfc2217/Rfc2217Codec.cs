using System.Buffers.Binary;
using System.Text;

namespace DevTerm.Transports.Rfc2217;

/// <summary>
/// Pure, no-I/O RFC 2217 wire format: Telnet IAC escaping and COM-PORT-OPTION subnegotiation
/// encode/decode. Never touches a socket, never throws on malformed input — a byte sequence
/// arriving off the wire is an expected, not exceptional, kind of "bad" input here.
/// </summary>
internal static class Rfc2217Codec
{
    /// <summary>Doubles every 0xFF byte (Telnet's IAC escaping) so it can safely ride inside a subnegotiation or the data channel.</summary>
    public static byte[] Escape(ReadOnlySpan<byte> data)
    {
        var extra = 0;
        foreach (var b in data)
        {
            if (b == Telnet.Iac)
            {
                extra++;
            }
        }

        if (extra == 0)
        {
            return data.ToArray();
        }

        var result = new byte[data.Length + extra];
        var index = 0;
        foreach (var b in data)
        {
            result[index++] = b;
            if (b == Telnet.Iac)
            {
                result[index++] = Telnet.Iac;
            }
        }

        return result;
    }

    /// <summary>
    /// Collapses doubled 0xFF pairs back to one. Assumes <paramref name="data"/> is already
    /// framing-stripped (no terminating IAC SE/IAC SB etc. left in it) — the caller finds message
    /// boundaries first, then unescapes only the bytes strictly between them.
    /// </summary>
    public static byte[] Unescape(ReadOnlySpan<byte> data)
    {
        if (data.IndexOf(Telnet.Iac) < 0)
        {
            return data.ToArray();
        }

        var result = new byte[data.Length];
        var writeIndex = 0;
        for (var i = 0; i < data.Length; i++)
        {
            result[writeIndex++] = data[i];
            if (data[i] == Telnet.Iac && i + 1 < data.Length && data[i + 1] == Telnet.Iac)
            {
                i++;
            }
        }

        return result[..writeIndex];
    }

    /// <summary>Worst-case encoded length (every payload byte happens to be 0xFF) for a command byte plus <paramref name="payloadLength"/> bytes of body.</summary>
    public static int GetMaxSubnegotiationLength(int payloadLength) => 3 + ((1 + payloadLength) * 2) + 2;

    /// <summary>Wraps a command code + payload as <c>IAC SB COM-PORT-OPTION &lt;escaped command+payload&gt; IAC SE</c>.</summary>
    public static byte[] EncodeSubnegotiation(byte commandCode, ReadOnlySpan<byte> payload)
    {
        var body = new byte[payload.Length + 1];
        body[0] = commandCode;
        payload.CopyTo(body.AsSpan(1));

        var escapedBody = Escape(body);
        var result = new byte[3 + escapedBody.Length + 2];
        result[0] = Telnet.Iac;
        result[1] = Telnet.Sb;
        result[2] = Telnet.ComPortOption;
        escapedBody.CopyTo(result.AsSpan(3));
        result[^2] = Telnet.Iac;
        result[^1] = Telnet.Se;
        return result;
    }

    public static byte[] EncodeSignatureRequest() => EncodeSubnegotiation(Rfc2217Command.Signature, ReadOnlySpan<byte>.Empty);

    public static byte[] EncodeSetBaudRate(int baudRate)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(value, baudRate);
        return EncodeSubnegotiation(Rfc2217Command.SetBaudRate, value);
    }

    public static byte[] EncodeSetDataSize(ComPortDataSize dataSize) => EncodeSubnegotiation(Rfc2217Command.SetDataSize, [(byte)dataSize]);

    public static byte[] EncodeSetParity(ComPortParity parity) => EncodeSubnegotiation(Rfc2217Command.SetParity, [(byte)parity]);

    public static byte[] EncodeSetStopSize(ComPortStopSize stopSize) => EncodeSubnegotiation(Rfc2217Command.SetStopSize, [(byte)stopSize]);

    public static byte[] EncodeSetControl(Rfc2217ControlValue value) => EncodeSubnegotiation(Rfc2217Command.SetControl, [(byte)value]);

    public static byte[] EncodePurgeData(Rfc2217PurgeTarget target) => EncodeSubnegotiation(Rfc2217Command.PurgeData, [(byte)target]);

    public static byte[] EncodeSetLineStateMask(Rfc2217LineState mask) => EncodeSubnegotiation(Rfc2217Command.SetLineStateMask, [(byte)mask]);

    public static byte[] EncodeSetModemStateMask(Rfc2217ModemState mask) => EncodeSubnegotiation(Rfc2217Command.SetModemStateMask, [(byte)mask]);

    /// <summary>
    /// Decodes an already de-escaped, already framing-stripped COM-PORT-OPTION subnegotiation body
    /// (command byte + its payload, no surrounding IAC SB/SE). Never throws:
    /// <list type="bullet">
    /// <item>empty input, or a recognized command whose payload is too short, yields <c>null</c>.</item>
    /// <item>an unrecognized command code yields <see cref="Rfc2217UnknownMessage"/>.</item>
    /// </list>
    /// </summary>
    public static IRfc2217Message? DecodeComPortMessage(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return null;
        }

        var rawCommand = payload[0];
        var isFromServer = rawCommand >= Rfc2217Command.ServerOffset;
        var baseCommand = (byte)(isFromServer ? rawCommand - Rfc2217Command.ServerOffset : rawCommand);
        var body = payload[1..];

        switch (baseCommand)
        {
            case Rfc2217Command.Signature:
                return new Rfc2217SignatureMessage(Encoding.ASCII.GetString(body), isFromServer);

            case Rfc2217Command.SetBaudRate:
                return body.Length < 4
                    ? null
                    : new Rfc2217BaudRateMessage(BinaryPrimitives.ReadInt32BigEndian(body), isFromServer);

            case Rfc2217Command.SetDataSize:
                return body.IsEmpty ? null : new Rfc2217DataSizeMessage((ComPortDataSize)body[0], isFromServer);

            case Rfc2217Command.SetParity:
                return body.IsEmpty ? null : new Rfc2217ParityMessage((ComPortParity)body[0], isFromServer);

            case Rfc2217Command.SetStopSize:
                return body.IsEmpty ? null : new Rfc2217StopSizeMessage((ComPortStopSize)body[0], isFromServer);

            case Rfc2217Command.SetControl:
                return body.IsEmpty ? null : new Rfc2217ControlMessage((Rfc2217ControlValue)body[0], isFromServer);

            case Rfc2217Command.NotifyLineState:
                return body.IsEmpty ? null : new Rfc2217NotifyLineStateMessage((Rfc2217LineState)body[0]);

            case Rfc2217Command.NotifyModemState:
                return body.IsEmpty ? null : new Rfc2217NotifyModemStateMessage((Rfc2217ModemState)body[0]);

            case Rfc2217Command.FlowControlSuspend:
                return new Rfc2217FlowControlSuspendMessage(isFromServer);

            case Rfc2217Command.FlowControlResume:
                return new Rfc2217FlowControlResumeMessage(isFromServer);

            case Rfc2217Command.SetLineStateMask:
                return body.IsEmpty ? null : new Rfc2217LineStateMaskMessage((Rfc2217LineState)body[0], isFromServer);

            case Rfc2217Command.SetModemStateMask:
                return body.IsEmpty ? null : new Rfc2217ModemStateMaskMessage((Rfc2217ModemState)body[0], isFromServer);

            case Rfc2217Command.PurgeData:
                return body.IsEmpty ? null : new Rfc2217PurgeDataMessage((Rfc2217PurgeTarget)body[0], isFromServer);

            default:
                return new Rfc2217UnknownMessage(rawCommand, body.ToArray());
        }
    }
}
