using DevTerm.UiDefinitions;

namespace DevTerm.Devices.Demo;

/// <summary>A tiny control panel for the loopback device: an LED toggle, a level slider and an Apply button. Exists so every front end's panel flow can be tested without hardware.</summary>
public static class DemoUiDefinition
{
    public static UiDefinition Build() => new()
    {
        Name = "Demo Device",
        Description = "A control panel for the loopback device: set an LED and a level, then Apply. Nothing is sent until Apply.",
        Sections =
        [
            new UiSection
            {
                Label = "Output",
                Controls =
                [
                    new ToggleControl { Id = "led", Label = "LED", DefaultValue = false },
                    new SliderControl { Id = "level", Label = "Level", Minimum = 0, Maximum = 9, Step = 1, DefaultValue = 0 },
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
