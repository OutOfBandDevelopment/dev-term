using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using DevTerm.UiDefinitions;
using DevTerm.UiDefinitions.Forms;

namespace DevTerm.DeviceManifests.Editing;

/// <summary>The manifest's own fields: identity, the transport hint, the command terminator, and how replies arrive.</summary>
[FormSection("Identity", Order = 0)]
[FormSection("Connection", Order = 1)]
[FormSection("Replies", Order = 2)]
public sealed class ManifestIdentityForm : EditorForm
{
    private readonly DeviceManifest _manifest;

    public ManifestIdentityForm(DeviceManifest manifest, Action edited)
        : base(edited)
    {
        _manifest = manifest;
    }

    public static IReadOnlyList<string> TransportTypes { get; } = ["", "serial", "tcp", "hid", "usbtmc", "loopback"];

    [Category("Identity")]
    [DisplayName("Name")]
    [Description("What pickers and the panel's title show.")]
    [FormField(Order = 0)]
    public string Name
    {
        get => _manifest.Name;
        set
        {
            _manifest.Name = value;
            Changed();
        }
    }

    [Category("Identity")]
    [DisplayName("Vendor")]
    [FormField(Order = 1)]
    public string? Vendor
    {
        get => _manifest.Vendor;
        set
        {
            _manifest.Vendor = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Identity")]
    [DisplayName("Version")]
    [FormField(Order = 2)]
    public string? Version
    {
        get => _manifest.Version;
        set
        {
            _manifest.Version = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Identity")]
    [DisplayName("Description")]
    [Description("Shown as the panel's Notes when the panel has none of its own.")]
    [FormField(Order = 3)]
    public string? Description
    {
        get => _manifest.Description;
        set
        {
            _manifest.Description = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Identity")]
    [DisplayName("Panel file")]
    [Description("Package mode: the panel is kept in this file beside the manifest (e.g. ui.json) instead of inline.")]
    [FormField(Order = 4)]
    public string? UiFile
    {
        get => _manifest.UiFile;
        set
        {
            _manifest.UiFile = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Connection")]
    [DisplayName("Transport hint")]
    [Description("A suggestion for the connection this device needs; blank for none.")]
    [FormField(Order = 0, OptionsFrom = nameof(TransportTypes))]
    public string TransportType
    {
        get => _manifest.Transport?.Type ?? string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _manifest.Transport = null;
            }
            else if (_manifest.Transport is { } hint)
            {
                hint.Type = value;
            }
            else
            {
                _manifest.Transport = new TransportHint { Type = value };
            }

            Changed();
        }
    }

    [Category("Connection")]
    [DisplayName("Transport options")]
    [Description("Key=value pairs, comma-separated (e.g. Baud=9600, DataBits=8).")]
    [FormField(Order = 1, VisibleWhen = nameof(HasTransport))]
    public string TransportOptions
    {
        get => string.Join(", ", (_manifest.Transport?.Options ?? []).Select(o => $"{o.Key}={o.Value}"));
        set
        {
            if (_manifest.Transport is not { } hint)
            {
                return;
            }

            hint.Options = [.. FormBinding.SplitList(value)
                .Select(pair => pair.Split('=', 2))
                .Where(kv => kv[0].Trim().Length > 0)
                .Select(kv => new TransportOption { Key = kv[0].Trim(), Value = kv.Length > 1 ? kv[1].Trim() : string.Empty })];
            Changed();
        }
    }

    public bool HasTransport => _manifest.Transport is not null;

    [Category("Connection")]
    [DisplayName("Command terminator")]
    [Description("Appended to every command sent, written with escapes: \\n, \\r\\n, \\x03 ...")]
    [FormField(Order = 2)]
    public string Terminator
    {
        get => Escape(_manifest.Terminator);
        set
        {
            _manifest.Terminator = Unescape(value);
            Changed();
        }
    }

    [Category("Replies")]
    [DisplayName("Replies end in CR/LF")]
    [Description("Off for a device whose replies have no terminator: each read is one reply.")]
    [FormField(Order = 0)]
    public bool LineTerminated
    {
        get => _manifest.Inbound?.LineTerminated ?? true;
        set
        {
            (_manifest.Inbound ??= new InboundProtocol()).LineTerminated = value;
            Changed();
        }
    }

    [Category("Replies")]
    [DisplayName("Kaitai layout file")]
    [Description("A binary protocol's .ksy file, beside the manifest (referenced only).")]
    [FormField(Order = 1)]
    public string? KaitaiFile
    {
        get => _manifest.Inbound?.KaitaiFile;
        set
        {
            var file = NullIfBlank(value);
            if (file is null && _manifest.Inbound is null)
            {
                return;
            }

            (_manifest.Inbound ??= new InboundProtocol()).KaitaiFile = file;
            Changed();
        }
    }
}

/// <summary>One outbound command: what it's called, what it sends, and whether a reply belongs to it.</summary>
[FormSection("Command", Order = 0, Label = "")]
public sealed class CommandForm : EditorForm
{
    private readonly DeviceManifest _manifest;

    public CommandForm(DeviceManifest manifest, OutboundCommand command, Action edited)
        : base(edited)
    {
        _manifest = manifest;
        Command = command;
    }

    public OutboundCommand Command { get; }

    [Category("Command")]
    [DisplayName("Name")]
    [FormField(Order = 0)]
    public string Name
    {
        get => Command.Name;
        set
        {
            Command.Name = value;
            Changed();
        }
    }

    [Category("Command")]
    [DisplayName("Id")]
    [Description("What a panel control invokes; the name when blank.")]
    [FormField(Order = 1)]
    public string? Id
    {
        get => Command.Id;
        set
        {
            Command.Id = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Command")]
    [DisplayName("Template")]
    [Description("The text sent, with {name} for each parameter ({value} when there are none); escapes like \\r allowed.")]
    [FormField(Order = 2)]
    public string Template
    {
        get => Escape(Command.Template);
        set
        {
            Command.Template = Unescape(value);
            Changed();
        }
    }

    [Category("Command")]
    [DisplayName("Is a query")]
    [Description("The next reply line belongs to this command and shows in its reply indicator.")]
    [FormField(Order = 3)]
    public bool IsQuery
    {
        get => Command.IsQuery;
        set
        {
            Command.IsQuery = value;
            Changed();
        }
    }

    [Category("Command")]
    [DisplayName("Reply id")]
    [Description("The indicator a query's reply lands in; {id}.reply when blank.")]
    [FormField(Order = 4)]
    public string? ReplyId
    {
        get => Command.ReplyId;
        set
        {
            Command.ReplyId = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Command")]
    [DisplayName("Sends")]
    [FormField(Order = 5, Kind = FormFieldKind.Indicator)]
    public string Sends => EditorForm.Escape(ManifestControlSurface.FormatTemplate(Command, null) + _manifest.Terminator)
        + (Command.EffectiveReplyId is { } reply ? $"   (reply → {reply})" : string.Empty);
}

/// <summary>One parameter of a command, substituted for <c>{Name}</c> in its template.</summary>
[FormSection("Parameter", Order = 0, Label = "")]
public sealed class ParameterForm : EditorForm
{
    public ParameterForm(CommandParameter parameter, Action edited)
        : base(edited)
    {
        Parameter = parameter;
    }

    public static IReadOnlyList<string> Types { get; } = ["string", "number", "integer"];

    public CommandParameter Parameter { get; }

    public bool IsNumeric => Parameter.IsNumeric;

    [Category("Parameter")]
    [DisplayName("Name")]
    [FormField(Order = 0)]
    public string Name
    {
        get => Parameter.Name;
        set
        {
            Parameter.Name = value;
            Changed();
        }
    }

    [Category("Parameter")]
    [DisplayName("Type")]
    [FormField(Order = 1, OptionsFrom = nameof(Types))]
    public string Type
    {
        get => Parameter.Type;
        set
        {
            Parameter.Type = value;
            Changed(null);
        }
    }

    [Category("Parameter")]
    [DisplayName("Minimum")]
    [FormField(Order = 2, VisibleWhen = nameof(IsNumeric))]
    public double? Minimum
    {
        get => Parameter.Minimum;
        set
        {
            Parameter.Minimum = value;
            Changed();
        }
    }

    [Category("Parameter")]
    [DisplayName("Maximum")]
    [FormField(Order = 3, VisibleWhen = nameof(IsNumeric))]
    public double? Maximum
    {
        get => Parameter.Maximum;
        set
        {
            Parameter.Maximum = value;
            Changed();
        }
    }

    [Category("Parameter")]
    [DisplayName("Unit")]
    [FormField(Order = 4)]
    public string? Unit
    {
        get => Parameter.Unit;
        set
        {
            Parameter.Unit = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Parameter")]
    [DisplayName("Default value")]
    [FormField(Order = 5)]
    public string? DefaultValue
    {
        get => Parameter.DefaultValue;
        set
        {
            Parameter.DefaultValue = NullIfBlank(value);
            Changed();
        }
    }

    [Category("Parameter")]
    [DisplayName("Number format")]
    [Description("A .NET format string, e.g. 00.00; shortest form when blank.")]
    [FormField(Order = 6, VisibleWhen = nameof(IsNumeric))]
    public string? Format
    {
        get => Parameter.Format;
        set
        {
            Parameter.Format = NullIfBlank(value);
            Changed();
        }
    }
}

/// <summary>One response pattern, plus an editor-only sample line to try it on.</summary>
[FormSection("Pattern", Order = 0, Label = "")]
[FormSection("Try it", Order = 1)]
public sealed class PatternForm : EditorForm
{
    private string _sample = string.Empty;

    public PatternForm(ResponsePattern pattern, Action edited)
        : base(edited)
    {
        Pattern = pattern;
    }

    public ResponsePattern Pattern { get; }

    [Category("Pattern")]
    [DisplayName("Name")]
    [Description("The value id the match publishes (its first capture group, or the whole match).")]
    [FormField(Order = 0)]
    public string Name
    {
        get => Pattern.Name;
        set
        {
            Pattern.Name = value;
            Changed();
            Notify(nameof(SampleResult));
        }
    }

    [Category("Pattern")]
    [DisplayName("Regex")]
    [Description("Tested against every complete reply line; each named group (?<id>...) publishes its own value.")]
    [FormField(Order = 1)]
    public string Match
    {
        get => Pattern.Match;
        set
        {
            Pattern.Match = value;
            Changed();
            Notify(nameof(SampleResult));
        }
    }

    [Category("Try it")]
    [DisplayName("Sample line")]
    [Description("Not saved: a reply line to test the regex on.")]
    [FormField(Order = 0)]
    public string SampleLine
    {
        get => _sample;
        set
        {
            _sample = value;
            OnSampleChanged();
        }
    }

    [Category("Try it")]
    [DisplayName("Publishes")]
    [FormField(Order = 1, Kind = FormFieldKind.Indicator)]
    public string SampleResult
    {
        get
        {
            if (_sample.Length == 0)
            {
                return "(type a sample line)";
            }

            try
            {
                var match = new Regex(Pattern.Match, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)).Match(_sample);
                if (!match.Success)
                {
                    return "no match";
                }

                var values = new List<string> { $"{Pattern.Name}={(match.Groups.Count > 1 ? match.Groups[1].Value : match.Value)}" };
                values.AddRange(match.Groups.Cast<Group>().Where(g => g.Success && !int.TryParse(g.Name, out _)).Select(g => $"{g.Name}={g.Value}"));
                return string.Join("  ", values);
            }
            catch (ArgumentException ex)
            {
                return "regex error: " + ex.Message;
            }
            catch (RegexMatchTimeoutException)
            {
                return "regex timed out";
            }
        }
    }

    // Editor-only: re-evaluates the sample without marking the manifest edited.
    private void OnSampleChanged()
    {
        Notify(nameof(SampleLine));
        Notify(nameof(SampleResult));
    }
}

/// <summary>The panel's own name and notes.</summary>
[FormSection("Panel", Order = 0, Label = "")]
public sealed class PanelForm : EditorForm
{
    public PanelForm(UiDefinition ui, Action edited)
        : base(edited)
    {
        Ui = ui;
    }

    public UiDefinition Ui { get; }

    [Category("Panel")]
    [DisplayName("Panel name")]
    [FormField(Order = 0)]
    public string Name
    {
        get => Ui.Name;
        set
        {
            Ui.Name = value;
            Changed();
        }
    }

    [Category("Panel")]
    [DisplayName("Notes")]
    [Description("Shown in the panel's collapsible Notes section; the manifest's description when blank.")]
    [FormField(Order = 1)]
    public string? Notes
    {
        get => Ui.Description;
        set
        {
            Ui.Description = NullIfBlank(value);
            Changed();
        }
    }
}

/// <summary>One labeled group of panel controls.</summary>
[FormSection("Section", Order = 0, Label = "")]
public sealed class SectionForm : EditorForm
{
    public SectionForm(UiSection section, Action edited)
        : base(edited)
    {
        Section = section;
    }

    public UiSection Section { get; }

    [Category("Section")]
    [DisplayName("Label")]
    [Description("The section header; blank for an always-open section with no header.")]
    [FormField(Order = 0)]
    public string? Label
    {
        get => Section.Label;
        set
        {
            Section.Label = NullIfBlank(value);
            Changed();
        }
    }
}
