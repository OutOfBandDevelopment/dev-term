using System.ComponentModel.DataAnnotations;

namespace DevTerm.Transports.Ble;

/// <summary>Configuration for a BLE peripheral connection. Bound via the Options pattern.</summary>
public sealed class BleTransportOptions
{
    /// <summary>
    /// The peripheral to connect to, in whatever opaque form the active platform backend's own
    /// <see cref="IBleDeviceDiscovery"/> produced (see <see cref="BleDeviceDescriptor.DeviceId"/>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>GATT service UUID. Defaults to the Nordic UART Service.</summary>
    public string ServiceUuid { get; set; } = NordicUartService.ServiceUuid;

    /// <summary>Characteristic written to for host-to-device bytes. Defaults to the NUS RX characteristic.</summary>
    public string WriteCharacteristicUuid { get; set; } = NordicUartService.WriteCharacteristicUuid;

    /// <summary>Characteristic subscribed to for device-to-host bytes. Defaults to the NUS TX characteristic.</summary>
    public string NotifyCharacteristicUuid { get; set; } = NordicUartService.NotifyCharacteristicUuid;

    /// <summary>Bounds a hung connect attempt so it fails with a <see cref="TimeoutException"/> instead of hanging forever.</summary>
    public int ConnectTimeoutMs { get; set; } = 10000;

    /// <summary>Bounds a blocked write so it fails with a <see cref="TimeoutException"/> instead of hanging forever.</summary>
    public int WriteTimeoutMs { get; set; } = 5000;
}
