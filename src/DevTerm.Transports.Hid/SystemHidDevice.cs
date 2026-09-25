using System.Linq;
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

    [Obsolete]
    public void Open()
    {
        // HidSharp's own GetHidDeviceOrNull(serialNumber:) only ever matches a device's real serial
        // descriptor - useless for a device like a Velleman K8055, which reports none at all. So the
        // candidates are enumerated by VID/PID only (serialNumber: null = wildcard) and matched here:
        // an exact SerialNumber match first (when non-blank - the more reliable identity, since a
        // real serial number is portable across USB ports), then an exact DevicePath match (tied to
        // a physical hub/port, the only thing that disambiguates multiple serial-less devices), then
        // just the first device found for this VID/PID.
        var candidates = DeviceList.Local.GetHidDevices(_options.VendorId, _options.ProductId, null, null).ToList();

        HidDevice? device = null;
        if (!string.IsNullOrEmpty(_options.SerialNumber))
        {
            device = candidates.FirstOrDefault(d => string.Equals(d.SerialNumber, _options.SerialNumber, StringComparison.Ordinal));
        }

        if (device is null && !string.IsNullOrEmpty(_options.DevicePath))
        {
            device = candidates.FirstOrDefault(d => string.Equals(d.DevicePath, _options.DevicePath, StringComparison.Ordinal));
        }

        device ??= candidates.FirstOrDefault();

        if (device is null)
        {
            var identity = _options.SerialNumber is not null ? $" serial '{_options.SerialNumber}'"
                : _options.DevicePath is not null ? $" device path '{_options.DevicePath}'"
                : string.Empty;
            throw new IOException($"No HID device found for VID 0x{_options.VendorId:X4} PID 0x{_options.ProductId:X4}{identity}.");
        }

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
