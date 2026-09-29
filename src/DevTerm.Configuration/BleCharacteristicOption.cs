using DevTerm.Transports.Ble;

namespace DevTerm.Configuration;

/// <summary>
/// One entry in <see cref="ConnectionEditorViewModel.BleCharacteristicOptions"/> — a single
/// characteristic under a single service, flattened from a front end's own GATT profile scan (see
/// <see cref="ConnectionEditorViewModel.SetBleCharacteristicOptions"/>) into one pickable row per
/// characteristic, formatted with its capability flags (Read/Write/Notify/...) for display
/// alongside the service/characteristic UUID pair that actually gets written into
/// <see cref="ConnectionEditorViewModel.BleServiceUuid"/>/<see cref="ConnectionEditorViewModel.BleWriteCharacteristicUuid"/>/
/// <see cref="ConnectionEditorViewModel.BleNotifyCharacteristicUuid"/> when picked.
/// </summary>
public sealed record BleCharacteristicOption(string Display, string ServiceUuid, string CharacteristicUuid)
{
    public static IEnumerable<BleCharacteristicOption> FromServices(IEnumerable<BleGattServiceDescriptor> services) =>
        services.SelectMany(service => service.Characteristics.Select(characteristic => new BleCharacteristicOption(
            FormatDisplay(service, characteristic),
            service.Uuid,
            characteristic.Uuid)));

    private static string FormatDisplay(BleGattServiceDescriptor service, BleGattCharacteristicDescriptor characteristic)
    {
        var flags = string.Join(",", new[]
        {
            characteristic.CanRead ? "Read" : null,
            characteristic.CanWrite ? "Write" : null,
            characteristic.CanWriteWithoutResponse ? "WriteWithoutResponse" : null,
            characteristic.CanNotify ? "Notify" : null,
            characteristic.CanIndicate ? "Indicate" : null,
        }.Where(f => f is not null));

        var name = characteristic.Name is null ? string.Empty : $"  {characteristic.Name}";
        return $"{characteristic.Uuid}  [{flags}]{name}  (service {service.Uuid})";
    }
}
