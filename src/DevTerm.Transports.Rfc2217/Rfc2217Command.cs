namespace DevTerm.Transports.Rfc2217;

/// <summary>
/// RFC 2217 §3 COM-PORT-OPTION command codes. A server-originated reply to any of these uses the
/// same code plus <see cref="ServerOffset"/> (e.g. the server's ack of a client's
/// <see cref="SetBaudRate"/> request is code 101) — this offset is what separates "the client is
/// asking" from "the server is answering/notifying" when both share one subnegotiation stream.
/// </summary>
internal static class Rfc2217Command
{
    public const byte Signature = 0;
    public const byte SetBaudRate = 1;
    public const byte SetDataSize = 2;
    public const byte SetParity = 3;
    public const byte SetStopSize = 4;
    public const byte SetControl = 5;
    public const byte NotifyLineState = 6;
    public const byte NotifyModemState = 7;
    public const byte FlowControlSuspend = 8;
    public const byte FlowControlResume = 9;
    public const byte SetLineStateMask = 10;
    public const byte SetModemStateMask = 11;
    public const byte PurgeData = 12;

    /// <summary>Added to a client-side command code to get the server's reply/notification code.</summary>
    public const byte ServerOffset = 100;
}
