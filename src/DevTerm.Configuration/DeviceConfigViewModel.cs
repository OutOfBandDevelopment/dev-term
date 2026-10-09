using DevTerm.Core.Control;
using DevTerm.UiDefinitions;

namespace DevTerm.Configuration;

/// <summary>
/// The Device &gt; Configure device flow behind the TUI and WPF screens (the web page has its own markup over the same
/// <see cref="DeviceConfigSession"/>): pick an editor, point it at a host and port, read the device's settings, edit the
/// editor's form fields, see exactly what will change, then write and check the read-back. Holds no UI types, so each front end
/// only renders <see cref="Fields"/> and calls <see cref="ReadAsync"/>/<see cref="WriteAsync"/>.
/// </summary>
public sealed class DeviceConfigViewModel(IReadOnlyList<IDeviceConfigEditor> editors, string host = "", string port = "")
{
    public IReadOnlyList<IDeviceConfigEditor> Editors { get; } = editors;

    public IDeviceConfigEditor? Editor { get; private set; } = editors.Count == 1 ? editors[0] : null;

    public string Host { get; set; } = host;

    public string Port { get; set; } = port;

    /// <summary>The live read/edit/write session; null until a successful <see cref="ReadAsync"/>.</summary>
    public DeviceConfigSession? Session { get; private set; }

    /// <summary>The last outcome in words (read, written, a failure), for a status line.</summary>
    public string? Message { get; private set; }

    public void PickEditor(string? id)
    {
        Editor = Editors.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        Session = null;
        Message = null;
    }

    /// <summary>The editor's editable controls, in form order (buttons are not settings).</summary>
    public IReadOnlyList<UiControl> Fields =>
        Editor is null ? [] : [.. Editor.BuildDefinition().Sections.SelectMany(section => section.Controls).Where(control => control is not ButtonControl)];

    public DeviceConfigTarget Target => new(Host.Trim(), int.TryParse(Port, out var port) ? port : 0);

    public string Value(string fieldId) => Session?.Values.GetValueOrDefault(fieldId) ?? string.Empty;

    public bool IsOn(string fieldId) => Value(fieldId) is "true" or "True" or "1";

    public void Set(string fieldId, string value) => Session?.Set(fieldId, value);

    public void SetOn(string fieldId, bool on) => Session?.Set(fieldId, on ? "true" : "false");

    /// <summary>One <c>field: from to to</c> line per changed value.</summary>
    public IReadOnlyList<string> ChangeLines() =>
        Session is null ? [] : [.. Session.Changes().Select(c => $"{c.FieldId}: {c.From} to {c.To}")];

    /// <summary>One <c>field: message</c> line per validation complaint.</summary>
    public IReadOnlyList<string> IssueLines() =>
        Session is null ? [] : [.. Session.Validate().Select(i => $"{i.FieldId}: {i.Message}")];

    public bool WillDropConnection => Session?.WillDropConnection() ?? false;

    public bool CanRead => Editor is not null && Host.Trim().Length > 0;

    public bool CanWrite => Session is { HasRead: true } session && session.Changes().Count > 0 && session.Validate().Count == 0;

    public async Task ReadAsync(CancellationToken cancellationToken = default)
    {
        if (Editor is null)
        {
            Message = "Choose an editor first.";
            return;
        }

        try
        {
            var session = new DeviceConfigSession(Editor, Target);
            await session.ReadAsync(cancellationToken).ConfigureAwait(false);
            Session = session;
            Message = "Read the device's current settings.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Session = null;
            Message = "Could not read the device: " + ex.Message;
        }
    }

    public async Task WriteAsync(CancellationToken cancellationToken = default)
    {
        if (Session is null)
        {
            Message = "Read the device's settings before writing.";
            return;
        }

        try
        {
            var mismatches = await Session.WriteAsync(cancellationToken).ConfigureAwait(false);
            Message = mismatches.Count == 0
                ? "Written; the device confirmed every value." + (Session.RebootRequired ? " Restart the device for the change to apply." : string.Empty)
                : "Written, but the device read back different values for: " + string.Join(", ", mismatches.Select(m => m.FieldId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = "Could not write the device: " + ex.Message;
        }
    }
}
