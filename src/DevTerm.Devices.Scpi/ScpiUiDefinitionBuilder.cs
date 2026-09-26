using System.Globalization;
using DevTerm.UiDefinitions;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// Maps a data-declared <see cref="ScpiInstrumentProfile"/> onto the same generic <see cref="UiDefinition"/>
/// model already proven against the K8055/Busylight — one <see cref="UiSection"/> per
/// <see cref="ScpiCommandDefinition.Category"/>, a parameter field per <see cref="ScpiParameterDefinition"/>
/// plus a button that invokes the command with those fields' values joined via
/// <see cref="ButtonControl.ParameterFieldIds"/>, and a query's reply indicator. Every profile also
/// gets an always-present "Custom Command" section so an uncurated command is never out of reach.
/// See docs/design/features/scpi-instrument-control.md.
/// </summary>
public static class ScpiUiDefinitionBuilder
{
    public const string CustomCommandFieldId = "customCommand";

    public static UiDefinition Build(ScpiInstrumentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var sections = profile.Commands
            .GroupBy(command => command.Category)
            .Select(group => new UiSection
            {
                Label = group.Key,
                Controls = [.. group.SelectMany(BuildControls)],
            })
            .ToList();

        sections.Add(new UiSection
        {
            Label = "Custom Command",
            Controls =
            [
                new TextFieldControl { Id = CustomCommandFieldId, Label = "Command" },
                new ButtonControl
                {
                    Id = ScpiControlSurface.SendCustomCommandId,
                    Label = "Send",
                    ParameterFieldIds = [CustomCommandFieldId],
                },
                new IndicatorControl { Id = $"{ScpiControlSurface.SendCustomCommandId}.reply", Label = "Reply" },
            ],
        });

        var baseDescription = $"SCPI instrument profile — {profile.Commands.Count} command(s).";
        return new UiDefinition
        {
            Name = profile.Name,
            Description = string.IsNullOrWhiteSpace(profile.Notes) ? baseDescription : $"{baseDescription} {profile.Notes}",
            Sections = sections,
        };
    }

    private static IEnumerable<UiControl> BuildControls(ScpiCommandDefinition command)
    {
        var parameterFieldIds = command.Parameters.Select(parameter => ParameterFieldId(command, parameter)).ToList();

        foreach (var parameter in command.Parameters)
        {
            yield return BuildParameterControl(command, parameter);
        }

        var hasParameters = command.Parameters.Count > 0;
        yield return new ButtonControl
        {
            Id = hasParameters ? $"{command.Id}.send" : command.Id,
            Label = command.Label,
            CommandId = hasParameters ? command.Id : null,
            ParameterFieldIds = hasParameters ? parameterFieldIds : null,
        };

        if (command.IsQuery)
        {
            yield return new IndicatorControl { Id = $"{command.Id}.reply", Label = $"{command.Label} Reply" };
        }
    }

    private static string ParameterFieldId(ScpiCommandDefinition command, ScpiParameterDefinition parameter) => $"{command.Id}.{parameter.Name}";

    /// <summary>
    /// The widget a parameter is collected with: <see cref="ScpiParameterDefinition.Control"/> when
    /// it fits the parameter's <see cref="ScpiParameterDefinition.Kind"/>, otherwise the kind's own
    /// default (numeric field / choice / text field).
    /// </summary>
    internal static ScpiParameterControl EffectiveControl(ScpiParameterDefinition parameter)
    {
        var kindDefault = parameter.Kind switch
        {
            ScpiParameterKind.Numeric => ScpiParameterControl.Numeric,
            ScpiParameterKind.Choice => ScpiParameterControl.Choice,
            _ => ScpiParameterControl.Text,
        };

        return parameter.Control switch
        {
            null => kindDefault,
            ScpiParameterControl.Numeric or ScpiParameterControl.Slider when parameter.Kind != ScpiParameterKind.Numeric => kindDefault,
            ScpiParameterControl.Choice when parameter.Options.Count == 0 => kindDefault,
            { } hint => hint,
        };
    }

    private static UiControl BuildParameterControl(ScpiCommandDefinition command, ScpiParameterDefinition parameter)
    {
        var id = ParameterFieldId(command, parameter);
        var defaultNumber = double.TryParse(parameter.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDefault)
            ? parsedDefault
            : parameter.Minimum;

        return EffectiveControl(parameter) switch
        {
            ScpiParameterControl.Numeric => new NumericControl
            {
                Id = id,
                Label = parameter.Name,
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                DefaultValue = defaultNumber,
                Unit = parameter.Unit,
            },
            ScpiParameterControl.Slider => new SliderControl
            {
                Id = id,
                Label = parameter.Name,
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                Step = parameter.DecimalPlaces is { } decimalPlaces ? Math.Pow(10, -decimalPlaces) : 1,
                DefaultValue = defaultNumber,
                Unit = parameter.Unit,
            },
            ScpiParameterControl.Choice => new ChoiceControl
            {
                Id = id,
                Label = parameter.Name,
                Options = parameter.Options,
                DefaultValue = parameter.DefaultValue,
            },
            _ => new TextFieldControl
            {
                Id = id,
                Label = parameter.Unit is { Length: > 0 } unit && parameter.Kind == ScpiParameterKind.Numeric ? $"{parameter.Name} ({unit})" : parameter.Name,
                DefaultValue = parameter.DefaultValue,
                Constraint = parameter.Kind == ScpiParameterKind.Numeric
                    ? new ValueConstraint
                    {
                        Kind = parameter.DecimalPlaces == 0 ? ValueKind.Integer : ValueKind.Number,
                        Minimum = parameter.Minimum,
                        Maximum = parameter.Maximum,
                    }
                    : null,
            },
        };
    }
}
