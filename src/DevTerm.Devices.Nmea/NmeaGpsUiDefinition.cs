using DevTerm.UiDefinitions;

namespace DevTerm.Devices.Nmea;

/// <summary>
/// A plain NMEA 0183 GPS receiver's control panel, per
/// docs/design/proposals/nmea-gps-protocol.md - read-only indicators driven live by
/// <see cref="NmeaGpsDecoder"/>, no buttons/toggles: like the DE-5000, a GPS receiver's output is
/// unprompted and one-directional, so there is nothing to send (see
/// <see cref="NmeaGpsControlSurface"/>).
/// </summary>
public static class NmeaGpsUiDefinition
{
    public static UiDefinition Build() => new()
    {
        Name = "NMEA GPS Receiver",
        Description = "NMEA 0183 sentence stream (GGA/RMC/GSA/GSV/VTG) - live fix, position, and satellite indicators. Confirmed compatible with the DeLorme Earthmate GPS BT-20 (USB HID, VID 0x1163/PID 0x0200). Read-only: a GPS receiver has no computer-controllable commands.",
        Sections =
        [
            new UiSection
            {
                Label = "Fix",
                Controls =
                [
                    new IndicatorControl { Id = "fixQuality", Label = "Fix quality", DefaultValue = "(no data)" },
                    new IndicatorControl { Id = "fixType", Label = "Fix type", DefaultValue = "(no data)" },
                    new IndicatorControl { Id = "navStatus", Label = "Nav status", DefaultValue = "(no data)" },
                ],
            },
            new UiSection
            {
                Label = "Position",
                Controls =
                [
                    new IndicatorControl { Id = "latitude", Label = "Latitude", DefaultValue = string.Empty },
                    new IndicatorControl { Id = "longitude", Label = "Longitude", DefaultValue = string.Empty },
                    new IndicatorControl { Id = "altitude", Label = "Altitude (m)", DefaultValue = string.Empty },
                    new IndicatorControl { Id = "speedKnots", Label = "Speed (kt)", DefaultValue = string.Empty },
                    new IndicatorControl { Id = "courseTrue", Label = "Course (true)", DefaultValue = string.Empty },
                ],
            },
            new UiSection
            {
                Label = "Satellites",
                Controls =
                [
                    new IndicatorControl { Id = "satellitesUsed", Label = "Satellites used", DefaultValue = "0" },
                    new IndicatorControl { Id = "satellitesInView", Label = "Satellites in view", DefaultValue = "0" },
                    new IndicatorControl { Id = "hdop", Label = "HDOP", DefaultValue = string.Empty },
                    new IndicatorControl { Id = "pdop", Label = "PDOP", DefaultValue = string.Empty },
                    new IndicatorControl { Id = "vdop", Label = "VDOP", DefaultValue = string.Empty },
                ],
            },
            new UiSection
            {
                Label = "Time",
                Controls =
                [
                    new IndicatorControl { Id = "utcTime", Label = "UTC time", DefaultValue = string.Empty },
                    new IndicatorControl { Id = "utcDate", Label = "UTC date", DefaultValue = string.Empty },
                ],
            },
        ],
    };
}
