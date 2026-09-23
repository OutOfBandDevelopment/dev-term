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
/// See docs/design/proposals/scpi-instrument-control.md.
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

        return new UiDefinition
        {
            Name = profile.Name,
            Description = $"SCPI instrument profile — {profile.Commands.Count} command(s).",
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

    private static UiControl BuildParameterControl(ScpiCommandDefinition command, ScpiParameterDefinition parameter)
    {
        var id = ParameterFieldId(command, parameter);
        return parameter.Kind switch
        {
            ScpiParameterKind.Numeric => new NumericControl
            {
                Id = id,
                Label = parameter.Name,
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                DefaultValue = double.TryParse(parameter.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var defaultNumber)
                    ? defaultNumber
                    : parameter.Minimum,
                Unit = parameter.Unit,
            },
            ScpiParameterKind.Choice => new ChoiceControl
            {
                Id = id,
                Label = parameter.Name,
                Options = parameter.Options,
                DefaultValue = parameter.DefaultValue,
            },
            _ => new TextFieldControl
            {
                Id = id,
                Label = parameter.Name,
                DefaultValue = parameter.DefaultValue,
            },
        };
    }
}
