using HidSharp;

namespace DevTerm.Transports.Hid;

/// <summary>
/// <see cref="IHidDevice"/> implementation backed by HidSharp. Deliberately thin — logic worth
/// unit testing belongs in <see cref="HidTransport"/>, which depends on the interface instead.
/// </summary>
public sealed class SystemHidDevice : IHidDevice
{
    private readonly HidTransportOptions _options;
    private HidStream? _stream;
    private HidReadStream? _readStream;

    public SystemHidDevice(HidTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public bool IsOpen => _stream is not null;

    public Stream BaseStream => _readStream
        ?? throw new InvalidOperationException("The HID device has not been opened.");

    public void Open()
    {
        var device = DeviceList.Local.GetHidDeviceOrNull(_options.VendorId, _options.ProductId, serialNumber: _options.SerialNumber)
            ?? throw new IOException(
                $"No HID device found for VID 0x{_options.VendorId:X4} PID 0x{_options.ProductId:X4}"
                + (_options.SerialNumber is null ? "." : $" serial '{_options.SerialNumber}'."));

        var stream = device.Open();
        stream.WriteTimeout = _options.WriteTimeoutMs;

        _stream = stream;
        _readStream = new HidReadStream(stream, _options.ReadTimeoutMs);
    }

    public void Close()
    {
        _readStream?.Stop();
        _readStream = null;

        _stream?.Close();
        _stream = null;
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("The HID device has not been opened.");
        }

        _stream.Write(offset == 0 && count == buffer.Length ? buffer : buffer[offset..(offset + count)]);
    }

    public void Dispose() => Close();
}
