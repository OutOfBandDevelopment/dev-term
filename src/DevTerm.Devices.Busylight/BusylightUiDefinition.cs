using DevTerm.UiDefinitions;

namespace DevTerm.Devices.Busylight;

/// <summary>
/// The Kuando Busylight's control panel, matching the mockup in
/// docs/design/features/kuando-busylight-protocol.md — a color preset, a blink mode plus raw
/// on/off byte fields, sound settings, and an Apply button (see <see cref="BusylightControlSurface"/>
/// for why nothing is sent until Apply is invoked).
/// </summary>
public static class BusylightUiDefinition
{
    public static UiDefinition Build() => new()
    {
        Name = "Kuando Busylight",
        Description = "USB HID status light — color, blink timing, and sound, sent as a single-command frame on Apply.",
        Sections =
        [
            new UiSection
            {
                Label = "Color",
                Controls =
                [
                    new ChoiceControl
                    {
                        Id = "color",
                        Label = "Color",
                        Style = ChoiceStyle.RadioGroup,
                        Options = ["Red", "Green", "Blue", "Yellow", "Custom", "Off"],
                        DefaultValue = "Off",
                    },
                    // "Custom" in the radio group above stands for the color picked here - picking a
                    // color selects it, and selecting it re-applies the picked color.
                    new ButtonControl { Id = "customColor", Label = "Custom...", ColorPickerTargetCommandId = "color", ColorPickerChoiceOption = "Custom" },
                ],
            },
            new UiSection
            {
                Label = "Blink",
                Controls =
                [
                    new ChoiceControl
                    {
                        Id = "blinkMode",
                        Label = "Blink",
                        Style = ChoiceStyle.RadioGroup,
                        Options = ["Solid", "Slow", "Fast"],
                        DefaultValue = "Solid",
                    },

                    // Unit unconfirmed per the proposal doc (only "On=0x01, Off=0x00 is solid" is
                    // confirmed) — raw protocol byte range, not necessarily milliseconds.
                    new NumericControl { Id = "onMs", Label = "On (unit unconfirmed)", Minimum = 0, Maximum = 255, DefaultValue = 1 },
                    new NumericControl { Id = "offMs", Label = "Off (unit unconfirmed)", Minimum = 0, Maximum = 255, DefaultValue = 0 },
                ],
            },
            new UiSection
            {
                Label = "Sound",
                Controls =
                [
                    new ToggleControl { Id = "mute", Label = "Mute", DefaultValue = false },
                    new ChoiceControl
                    {
                        Id = "track",
                        Label = "Track",
                        Style = ChoiceStyle.Dropdown,
                        Options = ["Funky", "Nordic", "Quiet", "Open Office", "Kuando"],
                        DefaultValue = "Funky",
                    },
                    new SliderControl { Id = "volume", Label = "Volume", Minimum = 0, Maximum = 7, Step = 1, DefaultValue = 0 },
                ],
            },
            new UiSection
            {
                Controls =
                [
                    new ButtonControl { Id = "apply", Label = "Apply" },
                ],
            },
        ],
    };
}
