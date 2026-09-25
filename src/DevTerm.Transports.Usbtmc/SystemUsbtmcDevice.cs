using System.Diagnostics;
using LibUsbDotNet;
using LibUsbDotNet.Info;
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
    private const byte _usbtmcInterfaceSubClass = 0x03;
    private const byte _usb488InterfaceProtocol = 0x01;

    // Endpoint descriptor Attributes bits 0-1 = transfer type (USB 2.0 spec table 9-13);
    // 0x02 = Bulk. A USBTMC interface can also expose an optional Interrupt-IN endpoint (for
    // USB488 SRQ) alongside Bulk-IN/-OUT, so endpoint selection must filter on this, not just
    // direction, or an Interrupt-IN sorting before Bulk-IN in the descriptor gets picked instead.
    private const byte _usbEndpointTransferTypeMask = 0x03;
    private const byte _usbEndpointTransferTypeBulk = 0x02;

    // bmRequestType for USBTMC class requests (USBTMC 1.0 section 4.2.1): Device-to-Host | Class,
    // addressed to the interface (INITIATE_CLEAR, GET_CAPABILITIES, the USB488 requests) or to a
    // bulk endpoint (the abort requests, wIndex = endpoint address).
    private const byte _classInterfaceRequestType = 0xA1;
    private const byte _classEndpointRequestType = 0xA2;

    // USBTMC 1.0 Table 15 / USB488 Table 9 bRequest values.
    private const byte _initiateAbortBulkOut = 1;
    private const byte _checkAbortBulkOutStatus = 2;
    private const byte _initiateAbortBulkIn = 3;
    private const byte _checkAbortBulkInStatus = 4;
    private const byte _initiateClear = 5;
    private const byte _checkClearStatus = 6;
    private const byte _getCapabilities = 7;
    private const byte _usb488RenControl = 160;
    private const byte _usb488GoToLocal = 161;

    // USBTMC 1.0 Table 16 USBTMC_status values.
    private const byte _statusSuccess = 0x01;
    private const byte _statusPending = 0x02;

    // GET_CAPABILITIES response (USB488 Table 8): byte 14 D1 = accepts REN_CONTROL/GO_TO_LOCAL.
    private const int _usb488InterfaceCapabilitiesOffset = 14;
    private const byte _usb488RenControlCapability = 0x02;

    // Bounds every CHECK_*_STATUS poll loop - a device stuck answering STATUS_PENDING must not
    // hang recovery (and with it the whole transport) forever.
    private const int _maxStatusPolls = 50;
    private const int _statusPollDelayMs = 20;

    private readonly UsbtmcTransportOptions _options;
    private UsbContext? _context;
    private IUsbDevice? _device;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;
    private int _interfaceNumber;
    private byte _bulkInAddress;
    private byte _bulkOutAddress;
    private bool _supportsRenControl;

    public SystemUsbtmcDevice(UsbtmcTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public bool IsOpen => _device?.IsOpen ?? false;

    public int MaxTransferSize => _options.MaxTransferSize;

    public int MaxPacketSize { get; private set; } = 64;

    public void Open()
    {
        var context = new UsbContext();
        IUsbDevice? matched = null;
        Exception? lastOpenFailure = null;

        try
        {
            // Every device context.List() hands back is a SafeHandle-backed native device
            // reference that must be disposed unless it's the one we keep - including every one
            // after the match - see the matching comment in SystemUsbtmcDeviceDiscovery.GetDevices()
            // for why (a real, confirmed native access-violation crash from undisposed devices
            // being finalized after their owning UsbContext was already freed).
            foreach (var candidate in context.List())
            {
                if (matched is null && IsCandidate(candidate))
                {
                    try
                    {
                        candidate.Open();

                        if (string.IsNullOrEmpty(_options.SerialNumber) ||
                            string.Equals(TryGetSerialNumber(candidate), _options.SerialNumber, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = candidate;
                            continue;
                        }

                        candidate.Close();
                    }
                    catch (Exception ex)
                    {
                        // e.g. an identical instrument with no WinUSB driver bound - try the next
                        // one rather than failing outright, but keep the reason in case none match.
                        lastOpenFailure = ex;
                        Debug.WriteLine($"USBTMC: failed to open a candidate device: {ex}");
                        try { candidate.Close(); } catch { }
                    }
                }

                candidate.Dispose();
            }

            if (matched is null)
            {
                throw new IOException(
                    $"No USBTMC device found for VID 0x{_options.VendorId:X4} PID 0x{_options.ProductId:X4}" +
                    (string.IsNullOrEmpty(_options.SerialNumber) ? string.Empty : $" serial '{_options.SerialNumber}'") +
                    (lastOpenFailure is null ? "." : $" (a matching device was found but could not be opened: {lastOpenFailure.Message})."),
                    lastOpenFailure);
            }

            var iface = FindUsbtmcInterface(matched);
            _interfaceNumber = iface.Number;

            matched.ClaimInterface(_interfaceNumber);

            var bulkIn = iface.Endpoints.FirstOrDefault(e =>
                (e.EndpointAddress & 0x80) != 0 && (e.Attributes & _usbEndpointTransferTypeMask) == _usbEndpointTransferTypeBulk)
                ?? throw new IOException("USBTMC interface has no bulk-IN endpoint.");
            var bulkOut = iface.Endpoints.FirstOrDefault(e =>
                (e.EndpointAddress & 0x80) == 0 && (e.Attributes & _usbEndpointTransferTypeMask) == _usbEndpointTransferTypeBulk)
                ?? throw new IOException("USBTMC interface has no bulk-OUT endpoint.");

            _bulkInAddress = bulkIn.EndpointAddress;
            _bulkOutAddress = bulkOut.EndpointAddress;

            // wMaxPacketSize bits 10..0 are the packet size (bits 12..11 are high-bandwidth
            // multipliers that don't apply to bulk endpoints).
            var packetSize = bulkIn.MaxPacketSize & 0x7FF;
            MaxPacketSize = packetSize > 0 ? packetSize : 64;

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

            _supportsRenControl = iface.Protocol == _usb488InterfaceProtocol && QueryRenControlSupport();
        }
        catch
        {
            if (matched is not null)
            {
                // Whether Close()/Dispose() implicitly releases a claimed interface depends on the
                // libusb backend - release explicitly first so a failure between ClaimInterface and
                // OpenEndpointReader/Writer doesn't leave the interface claimed by this process.
                try { matched.ReleaseInterface(_interfaceNumber); } catch { }
                try { matched.Close(); } catch { }
                try { matched.Dispose(); } catch { }
            }

            _reader = null;
            _writer = null;
            _device = null;
            _context = null;
            context.Dispose();
            throw;
        }
    }

    public void SetRemote(bool remote)
    {
        if (_device is null || !_supportsRenControl)
        {
            return;
        }

        try
        {
            // USB488 Table 15: REN_CONTROL's wValue *is* the REN state - 1 asserts it, 0
            // de-asserts it. Returning to local is GO_TO_LOCAL followed by de-asserting REN (the
            // spec's Figure 7 walkthrough); GO_TO_LOCAL alone leaves REN asserted, so the next
            // command puts the device straight back into remote.
            var response = new byte[1];
            if (remote)
            {
                ControlIn(_classInterfaceRequestType, _usb488RenControl, 1, _interfaceNumber, response);
            }
            else
            {
                ControlIn(_classInterfaceRequestType, _usb488GoToLocal, 0, _interfaceNumber, response);
                ControlIn(_classInterfaceRequestType, _usb488RenControl, 0, _interfaceNumber, response);
            }
        }
        catch (Exception ex) when (ex is UsbException or IOException)
        {
            Debug.WriteLine($"USBTMC: USB488 remote/local request failed: {ex}");
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

        if (error == Error.Timeout)
        {
            throw new TimeoutException($"USBTMC bulk-OUT transfer timed out after {_options.WriteTimeoutMs} ms (WriteTimeoutMs).");
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

    public int ReadBulkIn(byte[] buffer, out bool stalled)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("The USBTMC device is not open.");
        }

        var error = _reader.Read(buffer, _options.ReadTimeoutMs, out var transferred);
        stalled = error == Error.Pipe;
        if (stalled)
        {
            _reader.ClearHalt();
            error = _reader.Read(buffer, _options.ReadTimeoutMs, out transferred);
        }

        if (error == Error.Timeout)
        {
            if (stalled)
            {
                // The stall likely took the pending request with it - let the caller re-send it.
                return 0;
            }

            throw new TimeoutException($"USBTMC bulk-IN read timed out after {_options.ReadTimeoutMs} ms (ReadTimeoutMs).");
        }

        if (error != Error.Success)
        {
            throw new IOException($"USBTMC bulk-IN transfer failed: {error}.");
        }

        return transferred;
    }

    public void AbortBulkIn(byte bTag)
    {
        EnsureOpen();

        var response = new byte[2];
        ControlIn(_classEndpointRequestType, _initiateAbortBulkIn, bTag, _bulkInAddress, response);
        if (response[0] != _statusSuccess)
        {
            // STATUS_FAILED: nothing in progress. STATUS_TRANSFER_NOT_IN_PROGRESS: bTag didn't
            // match, or the FIFO still holds data. Either way there's no abort to wait on - just
            // make sure nothing is left queued on bulk-IN (USBTMC 1.0 Table 26).
            DrainBulkIn();
            return;
        }

        // Table 26 STATUS_SUCCESS: read until a short packet, then poll CHECK_ABORT_BULK_IN_STATUS.
        DrainBulkIn();
        var status = new byte[8];
        for (var i = 0; i < _maxStatusPolls; i++)
        {
            ControlIn(_classEndpointRequestType, _checkAbortBulkInStatus, 0, _bulkInAddress, status);
            if (status[0] != _statusPending)
            {
                return;
            }

            // bmAbortBulkIn.D0: the device still has queued bulk-IN data for the host to read.
            if ((status[1] & 0x01) != 0)
            {
                DrainBulkIn();
            }
            else
            {
                Thread.Sleep(_statusPollDelayMs);
            }
        }

        throw new IOException("USBTMC device did not finish aborting the bulk-IN transfer.");
    }

    public void AbortBulkOut(byte bTag)
    {
        EnsureOpen();

        var response = new byte[2];
        ControlIn(_classEndpointRequestType, _initiateAbortBulkOut, bTag, _bulkOutAddress, response);
        if (response[0] != _statusSuccess)
        {
            // Table 20: not in progress (or tag mismatch) - the device did not halt bulk-OUT.
            return;
        }

        var status = new byte[8];
        for (var i = 0; i < _maxStatusPolls; i++)
        {
            ControlIn(_classEndpointRequestType, _checkAbortBulkOutStatus, 0, _bulkOutAddress, status);
            if (status[0] != _statusPending)
            {
                // Table 23 STATUS_SUCCESS: the host must clear the halt the device set up.
                _writer!.ClearHalt();
                return;
            }

            Thread.Sleep(_statusPollDelayMs);
        }

        throw new IOException("USBTMC device did not finish aborting the bulk-OUT transfer.");
    }

    public void Clear()
    {
        EnsureOpen();

        var response = new byte[1];
        ControlIn(_classInterfaceRequestType, _initiateClear, 0, _interfaceNumber, response);
        if (response[0] != _statusSuccess)
        {
            throw new IOException($"USBTMC INITIATE_CLEAR was rejected (USBTMC_status 0x{response[0]:X2}).");
        }

        var status = new byte[2];
        for (var i = 0; i < _maxStatusPolls; i++)
        {
            ControlIn(_classInterfaceRequestType, _checkClearStatus, 0, _interfaceNumber, status);
            if (status[0] != _statusPending)
            {
                // Table 35: once the clear completes the host must clear the bulk-OUT halt the
                // device set up for it.
                _writer!.ClearHalt();
                return;
            }

            // bmClear.D0: queued bulk-IN data the device couldn't flush itself.
            if ((status[1] & 0x01) != 0)
            {
                DrainBulkIn();
            }
            else
            {
                Thread.Sleep(_statusPollDelayMs);
            }
        }

        throw new IOException("USBTMC device did not finish INITIATE_CLEAR.");
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

    private bool IsCandidate(IUsbDevice candidate) =>
        candidate.VendorId == _options.VendorId &&
        candidate.ProductId == _options.ProductId &&
        candidate.Configs.Any(config => config.Interfaces.Any(IsUsbtmcInterface));

    private static bool IsUsbtmcInterface(UsbInterfaceInfo iface) =>
        iface.Class == ClassCode.Application && iface.SubClass == _usbtmcInterfaceSubClass;

    // Prefers the device's active configuration - First() across every configuration could pick
    // an interface from one that isn't selected, whose endpoints don't exist right now.
    private static UsbInterfaceInfo FindUsbtmcInterface(IUsbDevice device)
    {
        UsbConfigInfo? active = null;
        try
        {
            var configurationValue = device.Configuration;
            active = device.Configs.FirstOrDefault(config => config.ConfigurationValue == configurationValue);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"USBTMC: could not read the active configuration: {ex}");
        }

        return active?.Interfaces.FirstOrDefault(IsUsbtmcInterface)
            ?? device.Configs.SelectMany(config => config.Interfaces).First(IsUsbtmcInterface);
    }

    private bool QueryRenControlSupport()
    {
        try
        {
            var capabilities = new byte[0x18];
            var count = ControlIn(_classInterfaceRequestType, _getCapabilities, 0, _interfaceNumber, capabilities);
            return count > _usb488InterfaceCapabilitiesOffset &&
                capabilities[0] == _statusSuccess &&
                (capabilities[_usb488InterfaceCapabilitiesOffset] & _usb488RenControlCapability) != 0;
        }
        catch (Exception ex) when (ex is UsbException or IOException)
        {
            Debug.WriteLine($"USBTMC: GET_CAPABILITIES failed: {ex}");
            return false;
        }
    }

    private int ControlIn(byte requestType, byte request, int value, int index, byte[] response)
    {
        var setup = new UsbSetupPacket(
            bRequestType: requestType,
            bRequest: request,
            wValue: value,
            wIndex: index,
            wlength: response.Length);
        return _device!.ControlTransfer(setup, response, 0, response.Length);
    }

    // Reads and discards bulk-IN data until a short packet (or nothing arrives within the read
    // timeout), so the next read starts at a fresh header.
    private void DrainBulkIn()
    {
        var buffer = new byte[UsbtmcCodec.BulkInBufferSize(_options.MaxTransferSize, MaxPacketSize)];
        for (var i = 0; i < _maxStatusPolls; i++)
        {
            var error = _reader!.Read(buffer, _options.ReadTimeoutMs, out var transferred);
            if (error == Error.Pipe)
            {
                _reader.ClearHalt();
                continue;
            }

            if (error != Error.Success || transferred < buffer.Length)
            {
                return;
            }
        }
    }

    private void EnsureOpen()
    {
        if (_device is null || _reader is null || _writer is null)
        {
            throw new InvalidOperationException("The USBTMC device is not open.");
        }
    }

    private static string? TryGetSerialNumber(IUsbDevice device)
    {
        try
        {
            // LibUsbDotNet's string-descriptor properties come back padded with trailing NUL
            // characters from the underlying fixed-size descriptor buffer - confirmed against real
            // hardware (a Rigol DS1102E's serial number read back as "DS1ET180300759\0"), which
            // silently fails an exact/case-insensitive match against a clean --serialnumber value.
            return device.Info.SerialNumber?.TrimEnd('\0');
        }
        catch (Exception ex)
        {
            // Not every candidate is fully enumerated at this point (e.g. a device the OS hasn't
            // finished binding a driver for) - swallowing this is correct, but silently is not: an
            // unexpected "every candidate rejected" result should be traceable back to this.
            Debug.WriteLine($"USBTMC: failed to read serial number: {ex}");
            return null;
        }
    }
}
