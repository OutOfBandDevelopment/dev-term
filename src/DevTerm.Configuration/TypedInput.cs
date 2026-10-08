using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Configuration;

/// <summary>
/// Turns a line typed into a front end's send field into the bytes to send, rejecting input the
/// selected parser can't encode (e.g. non-hex text with the hex parser) with a message for the
/// user instead of an exception. Shared by the CLI, TUI and WPF so an invalid line is handled -
/// reported, not sent, connection left alone - the same way everywhere.
/// </summary>
public static class TypedInput
{
    /// <summary>
    /// Encodes <paramref name="line"/> with <paramref name="input"/> and appends
    /// <paramref name="lineEnding"/>. Returns <see langword="false"/> with a user-facing
    /// <paramref name="error"/> if the parser rejects the line.
    /// </summary>
    public static bool TryEncode(IPresenterInput input, string parserName, string line, LineEnding lineEnding, out byte[] payload, out string? error)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            payload = lineEnding.Append(input.Parse(line));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            // What Convert.FromHexString/Convert.ToByte throw for text that isn't a valid number
            // in the parser's base, or a value that doesn't fit in a byte.
            payload = [];
            error = $"Not sent: '{line}' isn't valid {parserName} input ({ex.Message})";
            return false;
        }
    }

    /// <summary>
    /// Encodes <paramref name="line"/> with the options' effective parser looked up in <paramref name="catalog"/>, for
    /// callers (the control pipe) that have no send field. Returns the bytes, or an error message.
    /// </summary>
    public static (byte[]? Payload, string? Error) TryEncode(PresenterCatalog catalog, CliOptions options, string line)
    {
        var parser = options.EffectiveParser;
        if (!catalog.TryGetInput(parser, out var input))
        {
            return (null, $"No presenter named '{parser}' can encode text.");
        }

        return TryEncode(input, parser, line, options.LineEnding, out var payload, out var error) ? (payload, null) : (null, error);
    }

    /// <summary>
    /// Builds the text to echo for a sent line (View &gt; Echo Sent Commands): <paramref name="line"/>
    /// with <paramref name="lineEnding"/>'s literal characters appended, then
    /// <see cref="EscapeForDisplay"/>d - so e.g. a CR/LF line ending shows as the two visible
    /// characters <c>\r\n</c> instead of silently appended or breaking the echoed line in the output
    /// pane.
    /// </summary>
    public static string FormatForEcho(string line, LineEnding lineEnding) =>
        EscapeForDisplay(line + lineEnding.ToChars());

    /// <summary>
    /// Escapes every non-printable character in <paramref name="text"/> so it's safe to show as a
    /// single visible line: CR/LF/TAB/NUL become <c>\r</c>/<c>\n</c>/<c>\t</c>/<c>\0</c>, any other
    /// control character becomes <c>\xHH</c>, and a literal backslash is doubled so none of those
    /// escapes are ever ambiguous with a real backslash already in <paramref name="text"/>.
    /// </summary>
    public static string EscapeForDisplay(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var escape = c switch
            {
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(c) => $"\\x{(int)c:X2}",
                _ => null,
            };

            if (escape is null)
            {
                builder?.Append(c);
                continue;
            }

            builder ??= new StringBuilder(text[..i], text.Length + 8);
            builder.Append(escape);
        }

        return builder?.ToString() ?? text;
    }
}
