using System.Text.RegularExpressions;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>What <see cref="DeviceManifestValidator.Validate"/> found: errors a manifest can't load with, and warnings it can (but that would misbehave).</summary>
public sealed class DeviceManifestValidation
{
    public DeviceManifestValidation(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool IsValid => Errors.Count == 0;
}

/// <summary>A manifest <see cref="DeviceManifestLoader"/> refused because it failed <see cref="DeviceManifestValidator"/>'s checks.</summary>
public sealed class DeviceManifestValidationException : InvalidOperationException
{
    public DeviceManifestValidationException(IReadOnlyList<string> errors)
        : base("The device manifest isn't valid: " + string.Join(" ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// The one set of structural checks for a <see cref="DeviceManifest"/> — run by
/// <see cref="DeviceManifestLoader"/> on every load (an error fails the load) and by the manifest
/// editor before it saves (so it never writes a manifest that won't load back).
/// </summary>
/// <remarks>
/// <b>Errors</b> are what the manifest's own runtime pieces would otherwise throw on or silently
/// mis-route: a blank name; a command with no name or template; two commands with the same id (only
/// the first would ever run); a parameter with no name or a duplicate one; a response pattern with
/// no name, no regex, or one that doesn't compile. <b>Warnings</b> are what loads and opens but fails
/// when used: a button that invokes, or a field that commits, an id no command declares (the panel
/// reports "Unknown manifest command" on click), or a template placeholder no parameter fills.
/// </remarks>
public static partial class DeviceManifestValidator
{
    public static DeviceManifestValidation Validate(DeviceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            errors.Add("The manifest needs a name.");
        }

        var commandIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < manifest.OutboundCommands.Count; i++)
        {
            var command = manifest.OutboundCommands[i];
            var which = string.IsNullOrWhiteSpace(command.Name) ? $"Command {i + 1}" : $"Command '{command.Name}'";
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                errors.Add($"{which} needs a name.");
            }

            if (string.IsNullOrEmpty(command.Template))
            {
                errors.Add($"{which} needs a template (the text it sends).");
            }

            if (!string.IsNullOrWhiteSpace(command.EffectiveId) && !commandIds.Add(command.EffectiveId))
            {
                errors.Add($"{which}: another command already uses the id '{command.EffectiveId}'.");
            }

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in command.Parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter.Name))
                {
                    errors.Add($"{which} has a parameter with no name.");
                }
                else if (!parameterNames.Add(parameter.Name))
                {
                    errors.Add($"{which} has two parameters named '{parameter.Name}'.");
                }
            }

            foreach (Match token in Placeholder().Matches(command.Template ?? string.Empty))
            {
                var name = token.Groups[1].Value;
                if (!parameterNames.Contains(name) && !(name == "value" && command.Parameters.Count == 0))
                {
                    warnings.Add($"{which}: its template's '{{{name}}}' isn't one of its parameters, so it's sent as-is.");
                }
            }
        }

        for (var i = 0; i < (manifest.Inbound?.Patterns.Count ?? 0); i++)
        {
            var pattern = manifest.Inbound!.Patterns[i];
            var which = string.IsNullOrWhiteSpace(pattern.Name) ? $"Response pattern {i + 1}" : $"Response pattern '{pattern.Name}'";
            if (string.IsNullOrWhiteSpace(pattern.Name))
            {
                errors.Add($"{which} needs a name (the value id it publishes).");
            }

            if (string.IsNullOrEmpty(pattern.Match))
            {
                errors.Add($"{which} needs a regular expression to match.");
                continue;
            }

            try
            {
                _ = new Regex(pattern.Match, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"{which}: its regular expression doesn't compile ({ex.Message}).");
            }
        }

        if (manifest.Ui is { } ui)
        {
            ValidateUi(ui, commandIds, errors, warnings);
        }

        return new DeviceManifestValidation(errors, warnings);
    }

    private static void ValidateUi(UiDefinition ui, HashSet<string> commandIds, List<string> errors, List<string> warnings)
    {
        var controls = ui.Sections.SelectMany(s => s.Controls).ToList();
        var parameterFields = controls.OfType<ButtonControl>()
            .SelectMany(b => b.ParameterFieldIds ?? [])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var control in controls)
        {
            if (string.IsNullOrWhiteSpace(control.Id))
            {
                errors.Add($"The panel control '{control.Label}' needs an id.");
                continue;
            }

            switch (control)
            {
                case ButtonControl { ColorPickerTargetCommandId: { } target } when !commandIds.Contains(target):
                    warnings.Add($"Button '{control.Label}' sends its color to '{target}', which no command declares.");
                    break;
                case ButtonControl { ColorPickerTargetCommandId: null } button when !commandIds.Contains(button.CommandId ?? button.Id):
                    warnings.Add($"Button '{control.Label}' invokes '{button.CommandId ?? button.Id}', which no command declares.");
                    break;
                case ButtonControl { ParameterFieldIds: { } fields } button:
                    foreach (var missing in fields.Where(f => controls.All(c => c.Id != f)))
                    {
                        warnings.Add($"Button '{button.Label}' reads a parameter field '{missing}' the panel doesn't have.");
                    }

                    break;
                case ToggleControl or SliderControl or NumericControl or ChoiceControl or TextFieldControl
                    when !commandIds.Contains(control.Id) && !parameterFields.Contains(control.Id):
                    warnings.Add($"'{control.Label}' sends to '{control.Id}' when changed, but no command declares that id (and no button reads it as a parameter).");
                    break;
            }
        }
    }

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_.]*)\}")]
    private static partial Regex Placeholder();
}
