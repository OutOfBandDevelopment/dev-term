using System.Globalization;
using System.Text;
using DevTerm.Core.Control;
using DevTerm.Core.Sessions;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// <see cref="IControlSurface"/> for a <see cref="ScpiInstrumentProfile"/>: looks up the invoked
/// command by id, substitutes <c>{Name}</c> parameter tokens into its <see cref="ScpiCommandDefinition.Template"/>,
/// appends the profile's terminator, and sends the resulting ASCII bytes over the live
/// <see cref="Session"/>. Multiple parameter values arrive comma-joined in <paramref name="value"/>
/// (see <see cref="Value"/>) — see docs/design/features/scpi-instrument-control.md and
/// <see cref="DevTerm.UiDefinitions.ButtonControl.ParameterFieldIds"/>.
/// </summary>
public sealed class ScpiControlSurface : IControlSurface
{
    /// <summary>The always-present escape-hatch command id — see <see cref="ScpiUiDefinitionBuilder"/>. Sends its value verbatim, no template.</summary>
    public const string SendCustomCommandId = "sendCustom";

    private readonly Session _session;
    private readonly ScpiInstrumentProfile _profile;
    private readonly IScpiReplyTracker? _tracker;
    private readonly Dictionary<string, ScpiCommandDefinition> _commandsById;
    private readonly HashSet<string> _parameterFieldIds;

    public ScpiControlSurface(Session session, ScpiInstrumentProfile profile, IScpiReplyTracker? tracker)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(profile);

        _session = session;
        _profile = profile;
        _tracker = tracker;
        _commandsById = profile.Commands.ToDictionary(c => c.Id);

        // ScpiUiDefinitionBuilder gives each multi-parameter command's own fields an id of the form
        // "{command.Id}.{parameter.Name}" (see its ParameterFieldId) so they're unique across the
        // whole panel — they're value holders read by that command's own button
        // (ButtonControl.ParameterFieldIds), never commands in their own right. The generic
        // ControlPanelMode/ControlPanelWindow renderers commit every field on blur/Enter by calling
        // InvokeAsync with the field's own id, same as any standalone field — real-hardware-confirmed
        // on the KA6003P: tabbing off the "Set Voltage" field invoked "vset.Voltage" and this surface
        // threw. Recognizing that shape here and no-oping it is simpler than teaching the two generic
        // renderers about a per-field "don't auto-invoke" flag that would also affect other,
        // legitimately dual-purpose fields (see ControlPanelModeTests' shared fixture).
        _parameterFieldIds =
        [
            .. profile.Commands
                .Where(c => c.Parameters.Count > 0)
                .SelectMany(c => c.Parameters.Select(p => $"{c.Id}.{p.Name}")),
            // The always-present "Custom Command" text field (see ScpiUiDefinitionBuilder) is the
            // same shape of value-holder-not-a-command field, just for sendCustom rather than a
            // per-parameter button.
            ScpiUiDefinitionBuilder.CustomCommandFieldId,
        ];
    }

    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (commandId == SendCustomCommandId)
        {
            return SendAsync(value ?? string.Empty, $"{SendCustomCommandId}.reply", cancellationToken);
        }

        if (_parameterFieldIds.Contains(commandId))
        {
            return Task.CompletedTask;
        }

        if (!_commandsById.TryGetValue(commandId, out var command))
        {
            throw new ArgumentException($"Unknown SCPI command '{commandId}'.", nameof(commandId));
        }

        var text = BuildCommandText(command, value);
        return SendAsync(text, command.IsQuery ? $"{command.Id}.reply" : null, cancellationToken);
    }

    private static string BuildCommandText(ScpiCommandDefinition command, string? value)
    {
        var text = command.Template;
        if (command.Parameters.Count == 0)
        {
            return text;
        }

        var values = (value ?? string.Empty).Split(',');
        for (var i = 0; i < command.Parameters.Count; i++)
        {
            var parameter = command.Parameters[i];
            var raw = i < values.Length ? values[i] : parameter.DefaultValue ?? string.Empty;
            var formatted = parameter.Kind == ScpiParameterKind.Numeric
                ? FormatNumeric(raw, parameter)
                : raw;
            text = text.Replace("{" + parameter.Name + "}", formatted, StringComparison.Ordinal);
        }

        return text;
    }

    private static string FormatNumeric(string raw, ScpiParameterDefinition parameter)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            number = double.TryParse(parameter.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var fallback) ? fallback : 0;
        }

        var clamped = Math.Clamp(number, parameter.Minimum, parameter.Maximum);
        if (parameter.DecimalPlaces is not { } decimalPlaces)
        {
            return clamped.ToString(CultureInfo.InvariantCulture);
        }

        var integerDigits = Math.Max(parameter.IntegerDigits ?? 1, 1);
        var format = new string('0', integerDigits) + (decimalPlaces > 0 ? "." + new string('0', decimalPlaces) : string.Empty);
        return clamped.ToString(format, CultureInfo.InvariantCulture);
    }

    private Task SendAsync(string commandText, string? replyIndicatorId, CancellationToken cancellationToken)
    {
        if (replyIndicatorId is not null)
        {
            _tracker?.QuerySent(replyIndicatorId);
        }

        var bytes = Encoding.ASCII.GetBytes(commandText + _profile.Terminator);
        return _session.SendAsync(bytes, cancellationToken);
    }
}
