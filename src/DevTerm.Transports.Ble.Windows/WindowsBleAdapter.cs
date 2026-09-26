using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace DevTerm.Transports.Ble.Windows;

/// <summary>
/// <see cref="IBleAdapter"/> backed by <see cref="Windows.Devices.Bluetooth"/>'s WinRT GATT client
/// - the first (Windows) backend behind the adapter seam described in docs/design/transports.md's
/// "BLE" section. Not yet verified against real hardware - see TODO.md.
/// </summary>
public sealed class WindowsBleAdapter : IBleAdapter
{
    private readonly BleTransportOptions _options;
    private BluetoothLEDevice? _device;
    private GattCharacteristic? _writeCharacteristic;
    private GattCharacteristic? _notifyCharacteristic;

    public WindowsBleAdapter(BleTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public event EventHandler<ReadOnlyMemory<byte>>? NotificationReceived;

    public event EventHandler? Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var device = await BluetoothLEDevice.FromIdAsync(_options.DeviceId).AsTask(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No BLE device found for id '{_options.DeviceId}'.");

        try
        {
            var serviceUuid = Guid.Parse(_options.ServiceUuid);
            var writeUuid = Guid.Parse(_options.WriteCharacteristicUuid);
            var notifyUuid = Guid.Parse(_options.NotifyCharacteristicUuid);

            var servicesResult = await device.GetGattServicesForUuidAsync(serviceUuid).AsTask(cancellationToken).ConfigureAwait(false);
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                throw new InvalidOperationException($"BLE service '{_options.ServiceUuid}' was not found on device '{_options.DeviceId}'.");
            }

            var service = servicesResult.Services[0];

            var writeResult = await service.GetCharacteristicsForUuidAsync(writeUuid).AsTask(cancellationToken).ConfigureAwait(false);
            if (writeResult.Status != GattCommunicationStatus.Success || writeResult.Characteristics.Count == 0)
            {
                throw new InvalidOperationException($"BLE write characteristic '{_options.WriteCharacteristicUuid}' was not found.");
            }

            var notifyResult = await service.GetCharacteristicsForUuidAsync(notifyUuid).AsTask(cancellationToken).ConfigureAwait(false);
            if (notifyResult.Status != GattCommunicationStatus.Success || notifyResult.Characteristics.Count == 0)
            {
                throw new InvalidOperationException($"BLE notify characteristic '{_options.NotifyCharacteristicUuid}' was not found.");
            }

            var writeCharacteristic = writeResult.Characteristics[0];
            var notifyCharacteristic = notifyResult.Characteristics[0];

            notifyCharacteristic.ValueChanged += OnValueChanged;
            var notifyStatus = await notifyCharacteristic
                .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            if (notifyStatus != GattCommunicationStatus.Success)
            {
                notifyCharacteristic.ValueChanged -= OnValueChanged;
                throw new InvalidOperationException($"Failed to subscribe to the BLE notify characteristic: {notifyStatus}.");
            }

            _writeCharacteristic = writeCharacteristic;
            _notifyCharacteristic = notifyCharacteristic;
            device.ConnectionStatusChanged += OnConnectionStatusChanged;
            _device = device;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_writeCharacteristic is null)
        {
            throw new InvalidOperationException("The BLE adapter is not connected.");
        }

        // Prefer write-without-response when the characteristic supports it - most BLE Serial
        // (NUS-style) devices expose their RX characteristic this way, and a response round-trip
        // per byte chunk would otherwise throttle throughput for no benefit on those devices.
        var writeOption = _writeCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;

        var status = await _writeCharacteristic
            .WriteValueAsync(data.ToArray().AsBuffer(), writeOption)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        if (status != GattCommunicationStatus.Success)
        {
            throw new IOException($"BLE write failed: {status}.");
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Cleanup();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Cleanup();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args) =>
        NotificationReceived?.Invoke(this, args.CharacteristicValue.ToArray());

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Cleanup()
    {
        if (_notifyCharacteristic is not null)
        {
            _notifyCharacteristic.ValueChanged -= OnValueChanged;
            _notifyCharacteristic = null;
        }

        _writeCharacteristic = null;

        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }
    }
}
