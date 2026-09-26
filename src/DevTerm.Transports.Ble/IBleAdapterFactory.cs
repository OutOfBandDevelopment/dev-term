namespace DevTerm.Transports.Ble;

/// <summary>
/// Creates the <see cref="IBleAdapter"/> a <see cref="BleTransport"/> connects through. Indirection
/// point that lets tests substitute a fake adapter instead of touching a real Bluetooth stack, and
/// lets a per-OS backend (see <see cref="IBleAdapter"/>'s doc comment) register its own factory in
/// place of <see cref="UnsupportedPlatformBleAdapterFactory"/>.
/// </summary>
public interface IBleAdapterFactory
{
    IBleAdapter Create(BleTransportOptions options);
}

/// <summary>
/// The default registration when no per-OS BLE backend is available - resolvable, but fails clearly
/// at connect time rather than at DI registration time, and only once a user actually picks the
/// "ble" transport. See docs/design/transports.md's "BLE" section.
/// </summary>
public sealed class UnsupportedPlatformBleAdapterFactory : IBleAdapterFactory
{
    public IBleAdapter Create(BleTransportOptions options) =>
        throw new PlatformNotSupportedException(
            "No BLE adapter is available for this platform/build. On Windows this is picked up " +
            "automatically from DevTerm.Transports.Ble.Windows.dll when it's present next to the " +
            "executable; other platforms have no backend yet - see docs/design/transports.md's " +
            "\"BLE\" section.");
}
