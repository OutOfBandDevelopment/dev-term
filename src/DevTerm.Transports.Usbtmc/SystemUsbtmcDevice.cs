using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// The real <see cref="IUsbtmcDevice"/>, backed by LibUsbDotNet/libusb - see
/// docs/design/usbtmc-transport.md for why LibUsbDotNet was chosen over NI-VISA/IVI and the
/// real-hardware findings around needing a WinUSB (Zadig) driver rebind on Windows.
/// </summary>
public sealed class SystemUsbtmcDevice : IUsbtmcDevice
{
    private const byte UsbtmcInterfaceSubClass = 0x03;

    // USB488 subclass extension control requests (USBTMC USB488 spec, table 8) - bmRequestType
    // 0xA1 = Device-to-Host | Class | Interface, matching that spec's request definitions.
    private const byte Usb488RequestType = 0xA1;
    private const byte Usb488RenControl = 0xA0;
    private const byte Usb488GoToLocal = 0xA1;

    private readonly UsbtmcTransportOptions _options;
    private UsbContext? _context;
    private IUsbDevice? _device;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;
    private int _interfaceNumber;

    public SystemUsbtmcDevice(UsbtmcTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public bool IsOpen => _device?.IsOpen ?? false;

    public int MaxTransferSize => _options.MaxTransferSize;

    public void Open()
    {
        var context = new UsbContext();
        IUsbDevice? matched = null;

        try
        {
            foreach (var candidate in context.List())
            {
                // Every candidate context.List() hands back is a SafeHandle-backed native device
                // reference that must be disposed unless it's the one we keep - see the matching
                // comment in SystemUsbtmcDeviceDiscovery.GetDevices() for why (a real, confirmed
                // native access-violation crash from undisposed devices being finalized after
                // their owning UsbContext was already freed).
                if (candidate.VendorId != _options.VendorId || candidate.ProductId != _options.ProductId)
                {
                    candidate.Dispose();
                    continue;
                }

                var isUsbtmc = candidate.Configs.Any(config => config.Interfaces.Any(
                    iface => iface.Class == ClassCode.Application && iface.SubClass == UsbtmcInterfaceSubClass));
                if (!isUsbtmc)
                {
                    candidate.Dispose();
                    continue;
                }

                candidate.Open();

                if (!string.IsNullOrEmpty(_options.SerialNumber) &&
                    !string.Equals(TryGetSerialNumber(candidate), _options.SerialNumber, StringComparison.OrdinalIgnoreCase))
                {
                    candidate.Close();
                    candidate.Dispose();
                    continue;
                }

                matched = candidate;
                break;
            }

            if (matched is null)
            {
                throw new IOException(
                    $"No USBTMC device found for VID 0x{_options.VendorId:X4} PID 0x{_options.ProductId:X4}" +
                    (string.IsNullOrEmpty(_options.SerialNumber) ? string.Empty : $" serial '{_options.SerialNumber}'") + ".");
            }

            var iface = matched.Configs
                .SelectMany(config => config.Interfaces)
                .First(i => i.Class == ClassCode.Application && i.SubClass == UsbtmcInterfaceSubClass);
            _interfaceNumber = iface.Number;

            matched.ClaimInterface(_interfaceNumber);

            var bulkIn = iface.Endpoints.First(e => (e.EndpointAddress & 0x80) != 0);
            var bulkOut = iface.Endpoints.First(e => (e.EndpointAddress & 0x80) == 0);

            _reader = matched.OpenEndpointReader((ReadEndpointID)bulkIn.EndpointAddress, _options.MaxTransferSize, EndpointType.Bulk);
            _writer = matched.OpenEndpointWriter((WriteEndpointID)bulkOut.EndpointAddress, EndpointType.Bulk);

            // A prior run (or the device itself, on power-up) can leave a bulk endpoint halted -
            // confirmed against a real Rigol DM3000: the very first bulk-OUT write failed with
            // Error.Pipe (a STALL) until this was added. Best-effort since not every backend/device
            // supports CLEAR_FEATURE on an endpoint that isn't actually halted.
            try { _writer.ClearHalt(); } catch { }
            try { _reader.ClearHalt(); } catch { }

            _context = context;
            _device = matched;
        }
        catch
        {
            if (matched is not null)
            {
                try { matched.Close(); } catch { }
                try { matched.Dispose(); } catch { }
            }

            context.Dispose();
            throw;
        }
    }

    public void SetRemote(bool remote)
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            var setup = new UsbSetupPacket(
                bRequestType: Usb488RequestType,
                bRequest: remote ? Usb488RenControl : Usb488GoToLocal,
                wValue: remote ? 1 : 0,
                wIndex: _interfaceNumber,
                wlength: 1);
            var buffer = new byte[1];
            _device.ControlTransfer(setup, buffer, 0, buffer.Length);
        }
        catch (UsbException)
        {
            // Best-effort: not every USBTMC device implements the USB488 subclass extension.
        }
    }

    public void WriteBulkOut(byte[] transfer)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("The USBTMC device is not open.");
        }

        var error = _writer.Write(transfer, _options.WriteTimeoutMs, out var transferred);
        if (error == Error.Pipe)
        {
            // A STALL - clear it and retry once rather than failing outright, per
            // docs/design/usbtmc-transport.md's designed stall-recovery behavior.
            _writer.ClearHalt();
            error = _writer.Write(transfer, _options.WriteTimeoutMs, out transferred);
        }

        if (error != Error.Success)
        {
            throw new IOException($"USBTMC bulk-OUT transfer failed: {error}.");
        }

        if (transferred != transfer.Length)
        {
            throw new IOException($"USBTMC bulk-OUT transfer was short: wrote {transferred} of {transfer.Length} byte(s).");
        }
    }

    public int ReadBulkIn(byte[] buffer)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("The USBTMC device is not open.");
        }

        var error = _reader.Read(buffer, _options.ReadTimeoutMs, out var transferred);
        if (error == Error.Pipe)
        {
            _reader.ClearHalt();
            error = _reader.Read(buffer, _options.ReadTimeoutMs, out transferred);
        }

        if (error == Error.Timeout)
        {
            return 0;
        }

        if (error != Error.Success)
        {
            throw new IOException($"USBTMC bulk-IN transfer failed: {error}.");
        }

        return transferred;
    }

    public void Close()
    {
        if (_device is not null)
        {
            try { _device.ReleaseInterface(_interfaceNumber); } catch { }
            try { _device.Close(); } catch { }
            try { _device.Dispose(); } catch { }
        }

        _reader = null;
        _writer = null;
        _device = null;

        _context?.Dispose();
        _context = null;
    }

    public void Dispose() => Close();

    private static string? TryGetSerialNumber(IUsbDevice device)
    {
        try { return device.Info.SerialNumber; } catch { return null; }
    }
}
