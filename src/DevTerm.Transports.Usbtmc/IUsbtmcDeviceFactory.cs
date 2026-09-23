namespace DevTerm.Transports.Usbtmc;

/// <summary>
/// Creates the <see cref="IUsbtmcDevice"/> a <see cref="UsbtmcTransport"/> opens. Indirection
/// point that lets tests substitute a fake factory/device instead of touching real hardware.
/// </summary>
public interface IUsbtmcDeviceFactory
{
    IUsbtmcDevice Create(UsbtmcTransportOptions options);
}

public sealed class SystemUsbtmcDeviceFactory : IUsbtmcDeviceFactory
{
    public IUsbtmcDevice Create(UsbtmcTransportOptions options) => new SystemUsbtmcDevice(options);
}
