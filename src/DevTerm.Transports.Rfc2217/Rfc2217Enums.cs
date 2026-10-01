namespace DevTerm.Transports.Rfc2217;

/// <summary>RFC 2217 SET-DATASIZE wire values. <see cref="Request"/> asks the server to report the current value.</summary>
internal enum ComPortDataSize : byte
{
    Request = 0,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
}

/// <summary>RFC 2217 SET-PARITY wire values. <see cref="Request"/> asks the server to report the current value.</summary>
internal enum ComPortParity : byte
{
    Request = 0,
    None = 1,
    Odd = 2,
    Even = 3,
    Mark = 4,
    Space = 5,
}

/// <summary>RFC 2217 SET-STOPSIZE wire values. <see cref="Request"/> asks the server to report the current value.</summary>
internal enum ComPortStopSize : byte
{
    Request = 0,
    One = 1,
    Two = 2,
    OneAndAHalf = 3,
}

/// <summary>
/// RFC 2217 SET-CONTROL wire values — one shared subnegotiation command covering flow control and
/// the BREAK/DTR/RTS modem-control lines. Only the values dev-term's client actually needs to send
/// (DTR/RTS/BREAK set + "no flow control") plus the full request/ack set the codec must still be
/// able to decode when a server echoes one back.
/// </summary>
internal enum Rfc2217ControlValue : byte
{
    RequestOutboundFlowControl = 0,
    UseNoFlowControl = 1,
    UseXonXoffFlowControl = 2,
    UseHardwareFlowControl = 3,
    RequestBreakState = 4,
    SetBreakStateOn = 5,
    SetBreakStateOff = 6,
    RequestDtrState = 7,
    SetDtrStateOn = 8,
    SetDtrStateOff = 9,
    RequestRtsState = 10,
    SetRtsStateOn = 11,
    SetRtsStateOff = 12,
    RequestInboundFlowControl = 13,
    UseNoFlowControlInbound = 14,
    UseXonXoffFlowControlInbound = 15,
    UseHardwareFlowControlInbound = 16,
    UseDcdFlowControl = 17,
    UseDtrFlowControlInbound = 18,
    UseDsrFlowControl = 19,
}

/// <summary>RFC 2217 NOTIFY-LINESTATE bitmask (UART line-status register bits).</summary>
[Flags]
internal enum Rfc2217LineState : byte
{
    None = 0,
    DataReady = 0x01,
    OverrunError = 0x02,
    ParityError = 0x04,
    FramingError = 0x08,
    BreakDetect = 0x10,
    TransmitHoldingRegisterEmpty = 0x20,
    TransmitShiftRegisterEmpty = 0x40,
    TimeoutError = 0x80,
}

/// <summary>RFC 2217 NOTIFY-MODEMSTATE bitmask (UART modem-status register bits).</summary>
[Flags]
internal enum Rfc2217ModemState : byte
{
    None = 0,
    DeltaClearToSend = 0x01,
    DeltaDataSetReady = 0x02,
    TrailingEdgeRingIndicator = 0x04,
    DeltaReceiveLineSignalDetect = 0x08,
    ClearToSend = 0x10,
    DataSetReady = 0x20,
    RingIndicator = 0x40,
    ReceiveLineSignalDetect = 0x80,
}

/// <summary>RFC 2217 PURGE-DATA wire values.</summary>
internal enum Rfc2217PurgeTarget : byte
{
    ReceiveBuffer = 1,
    TransmitBuffer = 2,
    Both = 3,
}
