using System.Globalization;
using System.Text;

namespace DevTerm.Core.Control;

/// <summary>
/// Optional capability an <see cref="IControlSurface"/> can also implement: shows exactly what
/// <see cref="IControlSurface.InvokeAsync"/> would put on the wire for a given command id and value,
/// without sending anything or changing the surface's state. Both control-panel renderers use it for
/// a control's "sends" preview (a WPF info-icon tooltip, the TUI's footer line) — a separate
/// interface rather than a new <see cref="IControlSurface"/> member, so every existing surface (and
/// every test fake) keeps compiling unchanged and simply shows no preview.
/// </summary>
public interface ICommandPreview
{
    /// <summary>
    /// A human-readable rendering of what invoking <paramref name="commandId"/> with
    /// <paramref name="value"/> would send right now — e.g. <c>MEAS:VOLT:DC? DEF\n</c> for a text
    /// protocol (control characters escaped, see <see cref="CommandPreviewFormat.EscapeControlCharacters"/>)
    /// or hex report bytes for a binary one (see <see cref="CommandPreviewFormat.ToHex"/>). Null when
    /// that invocation sends nothing (a value-holder field, a state-only setter applied later) or the
    /// command id isn't one this surface knows. Never throws for a bad value; never sends.
    /// </summary>
    string? PreviewCommand(string commandId, string? value);
}

/// <summary>Shared formatting for <see cref="ICommandPreview"/> implementations, so every surface renders previews the same way.</summary>
public static class CommandPreviewFormat
{
    /// <summary>Escapes control characters visibly: <c>\r</c>, <c>\n</c>, <c>\t</c>, <c>\\</c>, and <c>\xNN</c> for any other control character.</summary>
    public static string EscapeControlCharacters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length + 4);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append("\\x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Space-separated upper-case hex bytes, e.g. <c>00 05 01 FF</c>.</summary>
    public static string ToHex(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return string.Join(' ', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
