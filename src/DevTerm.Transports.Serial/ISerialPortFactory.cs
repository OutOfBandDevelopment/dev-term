namespace DevTerm.Transports.Serial;

/// <summary>
/// Creates the <see cref="ISerialPort"/> a <see cref="SerialTransport"/> opens. Indirection
/// point that lets tests substitute a fake factory/port instead of touching real hardware.
/// </summary>
public interface ISerialPortFactory
{
    ISerialPort Create(SerialTransportOptions options);
}

public sealed class SystemSerialPortFactory : ISerialPortFactory
{
    public ISerialPort Create(SerialTransportOptions options) => new SystemSerialPort(options);
}
