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
/// (see <see cref="Value"/>) — see docs/design/proposals/scpi-instrument-control.md and
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

    public ScpiControlSurface(Session session, ScpiInstrumentProfile profile, IScpiReplyTracker? tracker)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(profile);

        _session = session;
        _profile = profile;
        _tracker = tracker;
        _commandsById = profile.Commands.ToDictionary(c => c.Id);
    }

    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (commandId == SendCustomCommandId)
        {
            return SendAsync(value ?? string.Empty, $"{SendCustomCommandId}.reply", cancellationToken);
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
