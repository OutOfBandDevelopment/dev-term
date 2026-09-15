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
            WriteTimeout = options.WriteTimeoutMs,
            ReadTimeout = options.ReadTimeoutMs,
            DtrEnable = options.DtrEnable,
        };

        // RtsEnable is under automatic flow-control management (and throws if set explicitly)
        // when Handshake already governs RTS.
        if (options.Handshake is not (System.IO.Ports.Handshake.RequestToSend or System.IO.Ports.Handshake.RequestToSendXOnXOff))
        {
            _port.RtsEnable = options.RtsEnable;
        }
    }

    public bool IsOpen => _port.IsOpen;

    public Stream BaseStream => new SerialPortReadStream(_port);

    public void Open() => _port.Open();

    public void Close() => _port.Close();

    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    public void Dispose() => _port.Dispose();
}
