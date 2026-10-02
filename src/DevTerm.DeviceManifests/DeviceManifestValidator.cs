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

        if (manifest.UiFile is { Length: > 0 } uiFile && !ManifestRelativePath.IsSafe(uiFile))
        {
            errors.Add($"The manifest's UiFile '{uiFile}' must be a path inside the manifest's own folder.");
        }

        if (manifest.Inbound?.KaitaiFile is { Length: > 0 } kaitaiFile && !ManifestRelativePath.IsSafe(kaitaiFile))
        {
            errors.Add($"The manifest's KaitaiFile '{kaitaiFile}' must be a path inside the manifest's own folder.");
        }

        if (manifest.Inbound?.Frame is { } frame)
        {
            errors.AddRange(frame.Validate());
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
            WarnOnUnknownReferences(ui, ValuePathCatalog.Enumerate(manifest), warnings);
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
                case StripChartControl { HistoryLength: > StripChartState.MaxCapacity } strip:
                    warnings.Add($"'{strip.Label}' declares a history length of {strip.HistoryLength}, which is clamped to {StripChartState.MaxCapacity}.");
                    break;
                case IndicatorControl { Expression: { Length: > 0 } expression } indicator when !Expression.TryParse(expression, out _, out var error):
                    errors.Add($"'{indicator.Label}' has an invalid expression: {error}");
                    break;
                case BarGraphControl { Channels: { } channels }:
                    ValidateChannelExpressions(control.Label, channels, errors);
                    break;
                case StripChartControl { Channels: { } channels }:
                    ValidateChannelExpressions(control.Label, channels, errors);
                    break;
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

            if (control is ButtonControl { ParameterExpressions: { } expressions } parameterButton)
            {
                for (var i = 0; i < expressions.Count; i++)
                {
                    if (expressions[i] is { Length: > 0 } expr && !Expression.TryParse(expr, out _, out var error))
                    {
                        errors.Add($"Button '{parameterButton.Label}' has an invalid parameter expression at index {i}: {error}");
                    }
                }
            }
        }
    }

    // An expression only ever reads ids the manifest can publish (ValuePathCatalog); one it can't is almost certainly a typo,
    // and evaluates as 0 at runtime without saying why. A warning, not an error: a decoder outside the manifest may still publish it.
    private static void WarnOnUnknownReferences(UiDefinition ui, IReadOnlyList<ValuePath> known, List<string> warnings)
    {
        var knownIds = known.Select(p => p.Path).ToHashSet(StringComparer.Ordinal);

        void Check(string owner, string? text)
        {
            if (text is not { Length: > 0 } || !Expression.TryParse(text, out var expression, out _))
            {
                return;
            }

            foreach (var id in expression!.ReferencedIds.Where(id => !knownIds.Contains(id)))
            {
                warnings.Add($"{owner}: its expression reads '{{{id}}}', which nothing in the manifest publishes.");
            }
        }

        foreach (var control in ui.Sections.SelectMany(s => s.Controls))
        {
            switch (control)
            {
                case IndicatorControl indicator:
                    Check($"'{indicator.Label}'", indicator.Expression);
                    break;
                case BarGraphControl { Channels: { } channels }:
                    channels.ForEach(ch => Check($"'{control.Label}' channel '{ch.Id}'", ch.Expression));
                    break;
                case StripChartControl { Channels: { } channels }:
                    channels.ForEach(ch => Check($"'{control.Label}' channel '{ch.Id}'", ch.Expression));
                    break;
                case ButtonControl { ParameterExpressions: { } expressions }:
                    foreach (var expr in expressions)
                    {
                        Check($"Button '{control.Label}'", expr);
                    }

                    break;
            }
        }
    }

    private static void ValidateChannelExpressions(string? label, IReadOnlyList<ChartChannel> channels, List<string> errors)
    {
        foreach (var channel in channels)
        {
            if (channel.Expression is { Length: > 0 } expr && !Expression.TryParse(expr, out _, out var error))
            {
                errors.Add($"'{label}' channel '{channel.Id}' has an invalid expression: {error}");
            }
        }
    }

    [GeneratedRegex(@"\{([A-Za-z_](?:[A-Za-z0-9_.]|\[[0-9]+\])*)\}")]
    private static partial Regex Placeholder();
}
