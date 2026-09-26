using System.Globalization;
using System.Text;
using DevTerm.Core.Control;
using DevTerm.Core.Sessions;
using DevTerm.Core.StreamContent;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// <see cref="IControlSurface"/> for a <see cref="ScpiInstrumentProfile"/>: looks up the invoked
/// command by id, substitutes <c>{Name}</c> parameter tokens into its <see cref="ScpiCommandDefinition.Template"/>,
/// appends the profile's terminator, and sends the resulting ASCII bytes over the live
/// <see cref="Session"/>. Multiple parameter values arrive comma-joined in <paramref name="value"/>
/// (see <see cref="Value"/>) — see docs/design/features/scpi-instrument-control.md and
/// <see cref="DevTerm.UiDefinitions.ButtonControl.ParameterFieldIds"/>.
/// </summary>
public sealed class ScpiControlSurface : IControlSurface, ICommandPreview
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

        if (Resolve(commandId, value) is not { } resolved)
        {
            return Task.CompletedTask;
        }

        if (resolved.ReplyIndicatorId is { } replyIndicatorId)
        {
            _tracker?.QuerySent(replyIndicatorId);
        }

        if (resolved.ResponseFormat != StreamContentFormat.Text)
        {
            // Found on the session's live pipeline rather than passed in, so nothing has to be
            // wired between a control panel and a Stream Monitor - no monitor running, no sink.
            foreach (var sink in _session.Presenters.OfType<IStreamContentHintSink>())
            {
                sink.ExpectResponse(resolved.ResponseFormat);
            }
        }

        var bytes = Encoding.ASCII.GetBytes(resolved.WireText);
        return SendAsync(bytes, resolved.ReplyIndicatorId, cancellationToken);
    }

    /// <summary>
    /// Sends the resolved bytes, cancelling the reply id <see cref="InvokeAsync"/> just registered
    /// via <see cref="DevTerm.Core.Presenters.IReplyTracker.QuerySent"/> if the send itself fails — otherwise that id stays
    /// queued forever waiting for a reply that will never arrive, shifting every later reply onto
    /// the wrong field. See docs/bugs/fixed/006-reply-queue-desync.md.
    /// </summary>
    private async Task SendAsync(byte[] bytes, string? replyIndicatorId, CancellationToken cancellationToken)
    {
        try
        {
            await _session.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (replyIndicatorId is not null)
            {
                _tracker?.Cancel(replyIndicatorId);
            }

            throw;
        }
    }

    /// <summary>
    /// The exact text <see cref="InvokeAsync"/> would send for <paramref name="commandId"/>/<paramref name="value"/>
    /// — same template substitution, numeric formatting, and terminator, via the same
    /// <see cref="Resolve"/> path — with the terminator and any other control character escaped
    /// visibly (e.g. <c>*IDN?\n</c>). Null for a value-holder field id or an unknown command.
    /// </summary>
    public string? PreviewCommand(string commandId, string? value)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (commandId != SendCustomCommandId && !_parameterFieldIds.Contains(commandId) && !_commandsById.ContainsKey(commandId))
        {
            return null;
        }

        return Resolve(commandId, value) is { } resolved
            ? CommandPreviewFormat.EscapeControlCharacters(resolved.WireText)
            : null;
    }

    /// <summary>
    /// The one place a command id + value becomes wire text (terminator included) — shared by
    /// <see cref="InvokeAsync"/> and <see cref="PreviewCommand"/> so a preview can never drift from
    /// what's actually sent. Null for a value-holder field id (sends nothing); throws for an unknown
    /// command id.
    /// </summary>
    private (string WireText, string? ReplyIndicatorId, StreamContentFormat ResponseFormat)? Resolve(string commandId, string? value)
    {
        if (commandId == SendCustomCommandId)
        {
            return ((value ?? string.Empty) + _profile.Terminator, $"{SendCustomCommandId}.reply", StreamContentFormat.Text);
        }

        if (_parameterFieldIds.Contains(commandId))
        {
            return null;
        }

        if (!_commandsById.TryGetValue(commandId, out var command))
        {
            throw new ArgumentException($"Unknown SCPI command '{commandId}'.", nameof(commandId));
        }

        return (BuildCommandText(command, value) + _profile.Terminator, command.IsQuery ? $"{command.Id}.reply" : null, command.ExpectedResponseFormat);
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
}
