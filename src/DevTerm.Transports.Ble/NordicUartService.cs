namespace DevTerm.Transports.Ble;

/// <summary>
/// The de facto "BLE Serial" GATT profile most hobbyist/embedded BLE devices use to emulate a UART
/// over two characteristics, per docs/design/transports.md's "BLE" section. <see cref="BleTransportOptions"/>
/// defaults to these UUIDs but they stay per-device configurable, since not every device that "acts
/// like serial over BLE" actually uses NUS.
/// </summary>
public static class NordicUartService
{
    public const string ServiceUuid = "6E400001-B5A3-F393-E0A9-E50E24DCCA9E";
    public const string WriteCharacteristicUuid = "6E400002-B5A3-F393-E0A9-E50E24DCCA9E";
    public const string NotifyCharacteristicUuid = "6E400003-B5A3-F393-E0A9-E50E24DCCA9E";
}
