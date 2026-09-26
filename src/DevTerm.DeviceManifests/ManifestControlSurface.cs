using System.Globalization;
using System.Text;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>
/// <see cref="IControlSurface"/> for a loaded <see cref="DeviceManifest"/>: the declarative
/// command/response schema executed over the live <see cref="Session"/>, the same shape as
/// <c>DevTerm.Devices.Scpi.ScpiControlSurface</c>. Looks up the invoked command by
/// <see cref="OutboundCommand.EffectiveId"/>, substitutes each <c>{Name}</c> parameter token (values
/// arrive comma-joined from a <c>ButtonControl.ParameterFieldIds</c> button; a single-value
/// control's value also fills a bare <c>{value}</c>), appends <see cref="DeviceManifest.Terminator"/>,
/// and sends the ASCII bytes. A query registers its reply id with the <see cref="IReplyTracker"/>
/// first, so the next complete line lands in that indicator. See docs/design/device-manifests.md.
/// </summary>
public sealed class ManifestControlSurface : IControlSurface, ICommandPreview
{
    private readonly Session? _session;
    private readonly DeviceManifest _manifest;
    private readonly IReplyTracker? _tracker;
    private readonly Dictionary<string, OutboundCommand> _commandsById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _passiveIds = new(StringComparer.Ordinal);

    /// <param name="session">The live session commands are sent over.</param>
    /// <param name="manifest">The manifest whose outbound commands this surface executes.</param>
    /// <param name="tracker">Correlates a query's reply line with its indicator (the manifest's reply presenter); null for none.</param>
    /// <param name="definition">The panel the surface serves (its parameter fields and display controls are no-op ids); the manifest's own <see cref="DeviceManifest.Ui"/> when null.</param>
    public ManifestControlSurface(Session session, DeviceManifest manifest, IReplyTracker? tracker, UiDefinition? definition = null)
        : this(manifest, definition, tracker)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    private ManifestControlSurface(DeviceManifest manifest, UiDefinition? definition, IReplyTracker? tracker)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        _manifest = manifest;
        _tracker = tracker;
        foreach (var command in manifest.OutboundCommands)
        {
            _commandsById.TryAdd(command.EffectiveId, command);
        }

        // A field that only feeds a parameter button, or a display control, isn't a command: the
        // generic renderers still commit a field on Enter/blur by its own id, so that's a no-op here
        // (the same rule ScpiControlSurface applies to its parameter fields) rather than an error.
        foreach (var control in ((definition ?? manifest.Ui)?.Sections ?? []).SelectMany(s => s.Controls))
        {
            if (control is ButtonControl { ParameterFieldIds: { } fieldIds })
            {
                _passiveIds.UnionWith(fieldIds.Where(id => !_commandsById.ContainsKey(id)));
            }

            if (control is (IndicatorControl or BarGraphControl or StripChartControl or VectorControl) &&!_commandsById.ContainsKey(control.Id))
            {
                _passiveIds.Add(control.Id);
            }
        }
    }

    /// <summary>
    /// A surface for a manifest that isn't connected to anything — the manifest editor's live panel
    /// preview: <see cref="PreviewCommand"/> works exactly as on a live panel, and invoking a command
    /// sends nothing, raising <see cref="PreviewInvoked"/> with what would have gone out instead.
    /// </summary>
    public static ManifestControlSurface ForPreview(DeviceManifest manifest, UiDefinition? definition = null) => new(manifest, definition, tracker: null);

    /// <summary>Raised (preview surfaces only, see <see cref="ForPreview"/>) with the escaped wire text an invoked command would have sent.</summary>
    public event EventHandler<string>? PreviewInvoked;

    public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (Resolve(commandId, value) is not { } resolved)
        {
            return Task.CompletedTask;
        }

        if (_session is null)
        {
            PreviewInvoked?.Invoke(this, CommandPreviewFormat.EscapeControlCharacters(resolved.WireText));
            return Task.CompletedTask;
        }

        if (resolved.ReplyId is { } replyId)
        {
            _tracker?.QuerySent(replyId);
        }

        return SendAsync(Encoding.ASCII.GetBytes(resolved.WireText), resolved.ReplyId, cancellationToken);
    }

    /// <summary>
    /// Sends the resolved bytes, cancelling the reply id <see cref="InvokeAsync"/> just registered
    /// via <see cref="IReplyTracker.QuerySent"/> if the send itself fails — otherwise that id stays
    /// queued forever waiting for a reply that will never arrive, shifting every later reply onto
    /// the wrong field. See docs/bugs/fixed/006-reply-queue-desync.md.
    /// </summary>
    private async Task SendAsync(byte[] bytes, string? replyIndicatorId, CancellationToken cancellationToken)
    {
        try
        {
            await _session!.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
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

    /// <summary>The exact text <see cref="InvokeAsync"/> would send, control characters escaped (<c>MEAS?\n</c>); null for a passive id or an unknown command.</summary>
    public string? PreviewCommand(string commandId, string? value)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        return _commandsById.ContainsKey(commandId) && Resolve(commandId, value) is { } resolved
            ? CommandPreviewFormat.EscapeControlCharacters(resolved.WireText)
            : null;
    }

    /// <summary>The one place a command id + value becomes wire text — shared by <see cref="InvokeAsync"/> and <see cref="PreviewCommand"/> so a preview can't drift from what's sent.</summary>
    private (string WireText, string? ReplyId)? Resolve(string commandId, string? value)
    {
        if (!_commandsById.TryGetValue(commandId, out var command))
        {
            if (_passiveIds.Contains(commandId))
            {
                return null;
            }

            throw new ArgumentException($"Unknown manifest command '{commandId}'.", nameof(commandId));
        }

        return (FormatTemplate(command, value) + _manifest.Terminator, command.EffectiveReplyId);
    }

    /// <summary>Substitutes <paramref name="value"/> (comma-joined, one per declared parameter, in order) into <paramref name="command"/>'s template.</summary>
    public static string FormatTemplate(OutboundCommand command, string? value)
    {
        ArgumentNullException.ThrowIfNull(command);

        var text = command.Template;
        if (command.Parameters.Count == 0)
        {
            return text.Replace("{value}", value ?? string.Empty, StringComparison.Ordinal);
        }

        var values = command.Parameters.Count == 1 ? [value ?? string.Empty] : (value ?? string.Empty).Split(',');
        for (var i = 0; i < command.Parameters.Count; i++)
        {
            var parameter = command.Parameters[i];
            var raw = i < values.Length && values[i].Length > 0 ? values[i] : parameter.DefaultValue ?? string.Empty;
            text = text.Replace("{" + parameter.Name + "}", parameter.IsNumeric ? FormatNumber(raw, parameter) : raw, StringComparison.Ordinal);
        }

        return text;
    }

    private static string FormatNumber(string raw, CommandParameter parameter)
    {
        if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            number = double.TryParse(parameter.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var fallback) ? fallback : 0;
        }

        number = Math.Clamp(number, parameter.Minimum ?? double.NegativeInfinity, parameter.Maximum ?? double.PositiveInfinity);
        if (parameter.IsInteger)
        {
            number = Math.Round(number, MidpointRounding.AwayFromZero);
        }

        return string.IsNullOrEmpty(parameter.Format)
            ? number.ToString(CultureInfo.InvariantCulture)
            : number.ToString(parameter.Format, CultureInfo.InvariantCulture);
    }
}
