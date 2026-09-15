namespace DevTerm.Transports.Hid;

/// <summary>
/// Creates the <see cref="IHidDevice"/> a <see cref="HidTransport"/> opens. Indirection point
/// that lets tests substitute a fake factory/device instead of touching real hardware.
/// </summary>
public interface IHidDeviceFactory
{
    IHidDevice Create(HidTransportOptions options);
}

public sealed class SystemHidDeviceFactory : IHidDeviceFactory
{
    public IHidDevice Create(HidTransportOptions options) => new SystemHidDevice(options);
}
