namespace DevTerm.Transports.Rfc2217;

/// <summary>A decoded COM-PORT-OPTION subnegotiation message. <see cref="IsFromServer"/> reflects whether the wire command code carried <see cref="Rfc2217Command.ServerOffset"/>.</summary>
internal interface IRfc2217Message
{
    bool IsFromServer { get; }
}

internal sealed record Rfc2217SignatureMessage(string Signature, bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217BaudRateMessage(int BaudRate, bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217DataSizeMessage(ComPortDataSize DataSize, bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217ParityMessage(ComPortParity Parity, bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217StopSizeMessage(ComPortStopSize StopSize, bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217ControlMessage(Rfc2217ControlValue Value, bool IsFromServer) : IRfc2217Message;

/// <summary>Server-pushed UART line-status change. Only ever server-originated, so <see cref="IsFromServer"/> is always <c>true</c>.</summary>
internal sealed record Rfc2217NotifyLineStateMessage(Rfc2217LineState LineState) : IRfc2217Message
{
    public bool IsFromServer => true;
}

/// <summary>Server-pushed UART modem-status change. Only ever server-originated, so <see cref="IsFromServer"/> is always <c>true</c>.</summary>
internal sealed record Rfc2217NotifyModemStateMessage(Rfc2217ModemState ModemState) : IRfc2217Message
{
    public bool IsFromServer => true;
}

internal sealed record Rfc2217FlowControlSuspendMessage(bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217FlowControlResumeMessage(bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217LineStateMaskMessage(Rfc2217LineState Mask, bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217ModemStateMaskMessage(Rfc2217ModemState Mask, bool IsFromServer) : IRfc2217Message;

internal sealed record Rfc2217PurgeDataMessage(Rfc2217PurgeTarget Target, bool IsFromServer) : IRfc2217Message;

/// <summary>An unrecognized command code or a payload too short for its command — decoding never throws, this is the fallback.</summary>
internal sealed record Rfc2217UnknownMessage(byte CommandCode, ReadOnlyMemory<byte> Payload) : IRfc2217Message
{
    public bool IsFromServer => CommandCode >= Rfc2217Command.ServerOffset;
}
