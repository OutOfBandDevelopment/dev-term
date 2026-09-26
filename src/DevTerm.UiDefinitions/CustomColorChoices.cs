namespace DevTerm.UiDefinitions;

/// <summary>
/// Finds the color-picker button that stands behind a choice option (see
/// <see cref="ButtonControl.ColorPickerChoiceOption"/>), so both renderers can link a "Custom" radio
/// option and its Custom... color button the same way.
/// </summary>
public static class CustomColorChoices
{
    /// <summary>Maps each linked choice control's id to its color button and the option that stands for it.</summary>
    public static IReadOnlyDictionary<string, (ButtonControl Button, string Option)> Find(UiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var controls = definition.Sections.SelectMany(section => section.Controls).ToList();
        var links = new Dictionary<string, (ButtonControl, string)>(StringComparer.Ordinal);
        foreach (var button in controls.OfType<ButtonControl>())
        {
            if (button is { ColorPickerTargetCommandId: { } target, ColorPickerChoiceOption: { } option }
                && controls.OfType<ChoiceControl>().Any(choice => choice.Id == target && choice.Options.Contains(option)))
            {
                links.TryAdd(target, (button, option));
            }
        }

        return links;
    }
}
