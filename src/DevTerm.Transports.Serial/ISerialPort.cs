namespace DevTerm.Transports.Serial;

/// <summary>
/// Thin abstraction over <see cref="System.IO.Ports.SerialPort"/> so <see cref="SerialTransport"/>
/// can be unit tested with a fake/mock instead of a real serial port.
/// </summary>
public interface ISerialPort : IDisposable
{
    bool IsOpen { get; }

    event EventHandler<SerialPortDataReceivedEventArgs>? DataReceived;

    void Open();

    void Close();

    void Write(byte[] buffer, int offset, int count);
}

public sealed class SerialPortDataReceivedEventArgs(ReadOnlyMemory<byte> data) : EventArgs
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}
