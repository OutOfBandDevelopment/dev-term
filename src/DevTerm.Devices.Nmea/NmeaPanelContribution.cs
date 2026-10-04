using DevTerm.Core.Control;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.Nmea;

/// <summary>The Nmea control panel as an <see cref="IDevicePanelContribution"/>.</summary>
public sealed class NmeaPanelContribution : IDevicePanelContribution
{
    public string Id => "nmea";

    public string MenuTitle => "_NMEA 0183...";

    public string? PresenterName => "nmea";

    // The confirmed unit (DeLorme Earthmate GPS BT-20) is a fixed VID/PID USB HID device.
    public bool IsAvailable(string transport, int vendorId, int productId) => string.Equals(transport, "hid", StringComparison.OrdinalIgnoreCase) && vendorId == 0x1163 && productId == 0x0200;

    public UiDefinition BuildDefinition() => NmeaGpsUiDefinition.Build();

    public IControlSurface CreateSurface(Session session) => new NmeaGpsControlSurface();
}
