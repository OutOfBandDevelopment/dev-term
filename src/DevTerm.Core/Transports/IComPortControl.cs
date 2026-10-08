namespace DevTerm.Core.Transports;

public enum ComParity
{
    None,
    Odd,
    Even,
    Mark,
    Space,
}

public enum ComStopBits
{
    One,
    OnePointFive,
    Two,
}

/// <summary>
/// Serial line settings that can change on a connection that stays open. Implemented by transports that
/// front a serial line (<c>SerialTransport</c>, <c>Rfc2217Transport</c>); reach it by pattern-matching
/// <see cref="DevTerm.Core.Sessions.Session.Transport"/>. Each setter applies at once if the connection is
/// open, is remembered for the next open either way, and returns the same instance so calls chain:
/// <c>control.SetBaudRate(115200).SetParity(ComParity.Even).SetDtr(false)</c>.
/// </summary>
public interface IComPortControl
{
    IComPortControl SetBaudRate(int baudRate);

    IComPortControl SetDataBits(int dataBits);

    IComPortControl SetParity(ComParity parity);

    IComPortControl SetStopBits(ComStopBits stopBits);

    IComPortControl SetDtr(bool enabled);

    IComPortControl SetRts(bool enabled);
}
