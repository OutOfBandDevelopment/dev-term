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
}
