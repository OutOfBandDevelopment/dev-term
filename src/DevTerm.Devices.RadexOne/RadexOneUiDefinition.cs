using DevTerm.UiDefinitions;

namespace DevTerm.Devices.RadexOne;

/// <summary>
/// The Radex One geiger counter's control panel: one-shot query buttons (data reading,
/// serial/version, current settings), a reset-accumulated action, plus an alarm-settings section
/// that only mutates local state
/// until Write Settings is pressed (see <see cref="RadexOneControlSurface"/> for the write-3x quirk).
/// Alarm mode options and threshold range are best-effort guesses — see
/// docs/design/proposals/radex-one-protocol.md's Status section.
/// </summary>
public static class RadexOneUiDefinition
{
    public static UiDefinition Build() => new()
    {
        Name = "Radex One",
        Description = "USB HID geiger counter — read live data, serial/version, and alarm settings.",
        Sections =
        [
            new UiSection
            {
                Label = "Read",
                Controls =
                [
                    new ButtonControl { Id = "readData", Label = "Read Data" },
                    new ButtonControl { Id = "readSerialVersion", Label = "Read Serial/Version" },
                    new ButtonControl { Id = "readSettings", Label = "Read Settings" },
                ],
            },
            new UiSection
            {
                Label = "Maintenance",
                Controls =
                [
                    new ButtonControl { Id = "resetAccumulated", Label = "Reset Accumulated" },
                ],
            },
            new UiSection
            {
                Label = "Alarm Settings",
                Controls =
                [
                    new ChoiceControl
                    {
                        Id = "alarmMode",
                        Label = "Alarm Mode",
                        Style = ChoiceStyle.RadioGroup,
                        Options = ["Off", "Vibration", "Audio", "Vibration+Audio"],
                        DefaultValue = "Off",
                    },

                    // Range unconfirmed — the proposal doc doesn't specify the device's min/max alarm
                    // threshold; 9999 is a generous upper bound for a µR/h-or-µSv/h-scale reading.
                    new NumericControl { Id = "threshold", Label = "Threshold (unit unconfirmed)", Minimum = 0, Maximum = 9999, DefaultValue = 0 },
                    new ButtonControl { Id = "writeSettings", Label = "Write Settings" },
                ],
            },
        ],
    };
}
