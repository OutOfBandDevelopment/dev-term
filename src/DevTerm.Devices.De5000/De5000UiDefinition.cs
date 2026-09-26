using DevTerm.UiDefinitions;

namespace DevTerm.Devices.De5000;

/// <summary>
/// The DE-5000 LCR meter's control panel, per
/// docs/design/proposals/de5000-lcr-meter-protocol.md - read-only indicators driven live by
/// <see cref="De5000Decoder"/>, no buttons/toggles: the meter's optical output is unprompted and
/// one-directional, so there is nothing to send (see <see cref="De5000ControlSurface"/>).
/// </summary>
public static class De5000UiDefinition
{
    public static UiDefinition Build() => new()
    {
        Name = "DE-5000 LCR Meter",
        Description = "Optical UART output over a BLE adapter (Cyrustek ES51919 chipset framing) - live primary/secondary readings, test frequency, and mode flags. Read-only: the meter has no computer-controllable commands.",
        Sections =
        [
            new UiSection
            {
                Label = "Measurement",
                Controls =
                [
                    new IndicatorControl { Id = "primary", Label = "Primary", DefaultValue = "(no data)" },
                    new IndicatorControl { Id = "secondary", Label = "Secondary", DefaultValue = "(no data)" },
                    new IndicatorControl { Id = "frequency", Label = "Frequency", DefaultValue = string.Empty },
                ],
            },
            new UiSection
            {
                Label = "Mode",
                Controls =
                [
                    new IndicatorControl { Id = "hold", Label = "Hold", DefaultValue = "0" },
                    new IndicatorControl { Id = "delta", Label = "Relative (Δ)", DefaultValue = "0" },
                    new IndicatorControl { Id = "referenceShown", Label = "Reference shown", DefaultValue = "0" },
                    new IndicatorControl { Id = "calibration", Label = "Calibration", DefaultValue = "0" },
                    new IndicatorControl { Id = "sorting", Label = "Sorting", DefaultValue = "0" },
                    new IndicatorControl { Id = "lcrAuto", Label = "Auto LCR", DefaultValue = "0" },
                    new IndicatorControl { Id = "autoRange", Label = "Auto range", DefaultValue = "0" },
                    new IndicatorControl { Id = "parallel", Label = "Parallel mode", DefaultValue = "0" },
                ],
            },
        ],
    };
}
