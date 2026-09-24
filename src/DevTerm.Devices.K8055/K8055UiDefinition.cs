using DevTerm.UiDefinitions;

namespace DevTerm.Devices.K8055;

/// <summary>
/// The Velleman K8055's control panel, matching the mockup in
/// docs/design/features/velleman-k8055-protocol.md — 8 digital outputs, 2 analog outputs (0-255),
/// a raw digital-input indicator (see <see cref="K8055Decoder"/>'s doc comment for why it's raw,
/// not 5 named channels), 2 analog-input indicators, and 2 pulse counters each with a reset button.
/// </summary>
public static class K8055UiDefinition
{
    public static UiDefinition Build() => new()
    {
        Name = "Velleman K8055",
        Description = "USB HID digital/analog I/O board — 8 digital out, 2 analog out, 5 digital in, 2 analog in, 2 pulse counters.",
        Sections =
        [
            new UiSection
            {
                Label = "Digital Out",
                Controls =
                [
                    .. Enumerable.Range(1, 8).Select(channel => new ToggleControl
                    {
                        Id = $"digitalOut{channel}",
                        Label = channel.ToString(),
                        DefaultValue = false,
                    }),
                ],
            },
            new UiSection
            {
                Label = "Analog Out",
                Controls =
                [
                    new SliderControl { Id = "analogOut1", Label = "Analog Out 1", Minimum = 0, Maximum = 255, Step = 1, DefaultValue = 0 },
                    new SliderControl { Id = "analogOut2", Label = "Analog Out 2", Minimum = 0, Maximum = 255, Step = 1, DefaultValue = 0 },
                ],
            },
            new UiSection
            {
                Label = "Digital In",
                Controls =
                [
                    new IndicatorControl { Id = "digitalInRaw", Label = "Digital In (raw)", DefaultValue = "0x00" },
                ],
            },
            new UiSection
            {
                Label = "Analog In",
                Controls =
                [
                    new IndicatorControl { Id = "analogIn1", Label = "Analog In 1", DefaultValue = "0" },
                    new IndicatorControl { Id = "analogIn2", Label = "Analog In 2", DefaultValue = "0" },
                ],
            },
            new UiSection
            {
                Label = "Counters",
                Controls =
                [
                    new IndicatorControl { Id = "counter1", Label = "Counter 1", DefaultValue = "0" },
                    new ButtonControl { Id = "resetCounter1", Label = "Reset" },
                    new IndicatorControl { Id = "counter2", Label = "Counter 2", DefaultValue = "0" },
                    new ButtonControl { Id = "resetCounter2", Label = "Reset" },
                ],
            },
        ],
    };
}
