namespace DevTerm.Transports.Serial;

/// <summary>Enumerates available serial ports, e.g. for a "--list-ports" CLI action or a GUI port picker.</summary>
public interface ISerialPortDiscovery
{
    IReadOnlyList<string> GetPortNames();
}

public sealed class SystemSerialPortDiscovery : ISerialPortDiscovery
{
    public IReadOnlyList<string> GetPortNames() => System.IO.Ports.SerialPort.GetPortNames();
}
