using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace DevTerm.DeviceManifests.Editing;

/// <summary>
/// Base of the manifest editor's form models: small annotated facades over one part of a
/// <see cref="DeviceManifest"/> (its identity, a command, a parameter, a panel control, ...) that
/// <c>FormDefinitionGenerator</c> turns into the forms both front ends render. Each setter writes the
/// manifest object directly and reports the edit, so the editor tracks unsaved changes and refreshes
/// its live panel preview; the facade itself holds no state of its own beyond editor-only helpers.
/// </summary>
public abstract class EditorForm : INotifyPropertyChanged
{
    private readonly Action _edited;

    protected EditorForm(Action edited)
    {
        _edited = edited;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A property changed; <paramref name="name"/> null means "re-read everything" (e.g. a control changed kind).</summary>
    protected void Changed([CallerMemberName] string? name = null)
    {
        Notify(name);
        _edited();
    }

    /// <summary>A property's shown value changed without the manifest changing (an editor-only helper field).</summary>
    protected void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Blank as null — an optional text left empty stays unset in the saved JSON, as it was.</summary>
    protected static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Text with its control characters written as escapes (<c>\n</c>, <c>\r</c>, <c>\t</c>, <c>\xHH</c>, and <c>\\</c> for a backslash) — how a template or terminator is edited in a one-line field.</summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 4);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '\\' => @"\\",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                < ' ' => string.Create(CultureInfo.InvariantCulture, $"\\x{(int)c:X2}"),
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    /// <summary>The inverse of <see cref="Escape"/>; an unrecognized escape is kept as typed.</summary>
    public static string Unescape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                builder.Append(c);
                continue;
            }

            var next = text[i + 1];
            switch (next)
            {
                case 'n':
                    builder.Append('\n');
                    i++;
                    break;
                case 'r':
                    builder.Append('\r');
                    i++;
                    break;
                case 't':
                    builder.Append('\t');
                    i++;
                    break;
                case '\\':
                    builder.Append('\\');
                    i++;
                    break;
                case 'x' when i + 3 < text.Length && byte.TryParse(text.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                    builder.Append((char)code);
                    i += 3;
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }
}
