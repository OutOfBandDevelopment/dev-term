using DevTerm.UiDefinitions;

namespace DevTerm.Devices.ZoomH4n;

/// <summary>
/// The Zoom H4n remote's control panel, matching the mockup in
/// docs/design/proposals/zoom-h4n-remote-protocol.md — transport buttons (Rwd/Play/Stop/FFwd/Record),
/// the remaining buttons (Vol+/Vol-/Rec+/Rec-/Mic/Ch1/Ch2), and 5 status indicators driven live by
/// <see cref="ZoomH4nDecoder"/> (ids prefixed "status" so they don't collide with the button command
/// ids of the same underlying word, e.g. the "record" button vs. the "statusRecord" indicator).
/// </summary>
public static class ZoomH4nUiDefinition
{
    public static UiDefinition Build() => new()
    {
        Name = "Zoom H4n Remote",
        Description = "RC04/RC2 remote protocol over serial (via the h4n2rs485 adapter) — transport controls, level/rec-level, mic/channel select, and live status LEDs.",
        Sections =
        [
            new UiSection
            {
                Label = "Transport",
                Controls =
                [
                    new ButtonControl { Id = "rwd", Label = "Rwd" },
                    new ButtonControl { Id = "play", Label = "Play" },
                    new ButtonControl { Id = "stop", Label = "Stop" },
                    new ButtonControl { Id = "ffwd", Label = "FFwd" },
                    new ButtonControl { Id = "record", Label = "Record" },
                ],
            },
            new UiSection
            {
                Label = "Levels / Input",
                Controls =
                [
                    new ButtonControl { Id = "volDown", Label = "Vol-" },
                    new ButtonControl { Id = "volUp", Label = "Vol+" },
                    new ButtonControl { Id = "recDown", Label = "Rec-" },
                    new ButtonControl { Id = "recUp", Label = "Rec+" },
                    new ButtonControl { Id = "mic", Label = "Mic" },
                    new ButtonControl { Id = "ch1", Label = "Ch1" },
                    new ButtonControl { Id = "ch2", Label = "Ch2" },
                ],
            },
            new UiSection
            {
                Label = "Status",
                Controls =
                [
                    new IndicatorControl { Id = "statusRecord", Label = "Record", DefaultValue = "0" },
                    new IndicatorControl { Id = "statusPeak", Label = "Peak", DefaultValue = "0" },
                    new IndicatorControl { Id = "statusMic", Label = "Mic", DefaultValue = "0" },
                    new IndicatorControl { Id = "statusLed1", Label = "Led1", DefaultValue = "0" },
                    new IndicatorControl { Id = "statusLed2", Label = "Led2", DefaultValue = "0" },
                ],
            },
        ],
    };
}
