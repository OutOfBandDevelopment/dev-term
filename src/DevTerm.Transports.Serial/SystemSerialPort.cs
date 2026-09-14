namespace DevTerm.Transports.Serial;

/// <summary>
/// <see cref="ISerialPort"/> implementation backed by the real
/// <see cref="System.IO.Ports.SerialPort"/>. Deliberately thin — logic that's worth unit
/// testing belongs in <see cref="SerialTransport"/>, which depends on the interface instead.
/// </summary>
public sealed class SystemSerialPort : ISerialPort
{
    private readonly System.IO.Ports.SerialPort _port;

    public SystemSerialPort(SerialTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _port = new System.IO.Ports.SerialPort(options.PortName, options.BaudRate, options.Parity, options.DataBits, options.StopBits)
        {
            Handshake = options.Handshake,
        };
        _port.DataReceived += OnDataReceived;
    }

    public bool IsOpen => _port.IsOpen;

    public event EventHandler<SerialPortDataReceivedEventArgs>? DataReceived;

    public void Open() => _port.Open();

    public void Close() => _port.Close();

    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    private void OnDataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
    {
        var bytesToRead = _port.BytesToRead;
        if (bytesToRead <= 0)
        {
            return;
        }

        var buffer = new byte[bytesToRead];
        var read = _port.Read(buffer, 0, bytesToRead);
        DataReceived?.Invoke(this, new SerialPortDataReceivedEventArgs(buffer.AsMemory(0, read)));
    }

    public void Dispose()
    {
        _port.DataReceived -= OnDataReceived;
        _port.Dispose();
    }
}
