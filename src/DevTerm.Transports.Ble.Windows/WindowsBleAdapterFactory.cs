namespace DevTerm.Transports.Ble.Windows;

public sealed class WindowsBleAdapterFactory : IBleAdapterFactory
{
    public IBleAdapter Create(BleTransportOptions options) => new WindowsBleAdapter(options);
}
