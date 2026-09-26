using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>
/// The panel a manifest opens as: its own <see cref="DeviceManifest.Ui"/> when it has one, otherwise
/// one generated from its outbound commands (a button per command, a field per parameter feeding
/// that button, and a reply indicator per query) — so a manifest that only declares commands still
/// gets a working panel. The manifest's <see cref="DeviceManifest.Description"/> fills in the
/// definition's Notes when the UI has none of its own.
/// </summary>
public static class ManifestUiBuilder
{
    public static UiDefinition Build(DeviceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var definition = manifest.Ui ?? new UiDefinition
        {
            Name = manifest.Name,
            Sections = [new UiSection { Label = "Commands", Controls = [.. manifest.OutboundCommands.SelectMany(CommandControls)] }],
        };

        if (string.IsNullOrWhiteSpace(definition.Description) && !string.IsNullOrWhiteSpace(manifest.Description))
        {
            definition.Description = manifest.Description;
        }

        return definition;
    }

    /// <summary>A parameter field's id in a generated panel: <c>{commandId}.{parameterName}</c>, unique across the panel.</summary>
    public static string ParameterFieldId(OutboundCommand command, CommandParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(parameter);
        return $"{command.EffectiveId}.{parameter.Name}";
    }

    private static IEnumerable<UiControl> CommandControls(OutboundCommand command)
    {
        foreach (var parameter in command.Parameters)
        {
            var label = parameter.Unit is { Length: > 0 } unit ? $"{parameter.Name} ({unit})" : parameter.Name;
            yield return new TextFieldControl
            {
                Id = ParameterFieldId(command, parameter),
                Label = label,
                DefaultValue = parameter.DefaultValue,
                Constraint = parameter.IsNumeric
                    ? new ValueConstraint
                    {
                        Kind = parameter.IsInteger ? ValueKind.Integer : ValueKind.Number,
                        Minimum = parameter.Minimum,
                        Maximum = parameter.Maximum,
                    }
                    : null,
            };
        }

        yield return new ButtonControl
        {
            Id = $"{command.EffectiveId}.send",
            Label = command.Name,
            CommandId = command.EffectiveId,
            ParameterFieldIds = command.Parameters.Count > 0 ? [.. command.Parameters.Select(p => ParameterFieldId(command, p))] : null,
        };

        if (command.EffectiveReplyId is { } replyId)
        {
            yield return new IndicatorControl { Id = replyId, Label = $"{command.Name} Reply" };
        }
    }
}
