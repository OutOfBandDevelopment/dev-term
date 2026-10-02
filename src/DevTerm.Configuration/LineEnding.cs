namespace DevTerm.Configuration;

/// <summary>
/// What to append to a typed line before sending it, when the presenter can encode outgoing
/// text (<see cref="DevTerm.Core.Presenters.IPresenterInput"/>). Left as <see cref="None"/> by
/// default so numeric-base presenters (hex/decimal/octal/binary), where a typed line is meant to
/// be exact bytes, aren't given a stray extra byte — set this explicitly for text-mode sessions
/// where pressing Enter is expected to send CR/LF like a real terminal.
/// </summary>
public enum LineEnding
{
    None,
    Cr,
    Lf,
    CrLf,
}

public static class LineEndingExtensions
{
    public static byte[] ToBytes(this LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.None => [],
        LineEnding.Cr => [0x0D],
        LineEnding.Lf => [0x0A],
        LineEnding.CrLf => [0x0D, 0x0A],
        _ => throw new ArgumentOutOfRangeException(nameof(lineEnding), lineEnding, message: null),
    };

    /// <summary>Returns <paramref name="payload"/> with this line ending's bytes appended (unchanged if <see cref="LineEnding.None"/>).</summary>
    public static byte[] Append(this LineEnding lineEnding, byte[] payload)
    {
        var terminator = lineEnding.ToBytes();
        return terminator.Length == 0 ? payload : [.. payload, .. terminator];
    }

    /// <summary>
    /// The literal characters <see cref="Append"/> would add as raw bytes (an actual CR and/or LF,
    /// not the two-character escape sequence) - for building the full text of what a sent line looked
    /// like before escaping it for display with <see cref="TypedInput.EscapeForDisplay"/>.
    /// </summary>
    public static string ToChars(this LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.None => string.Empty,
        LineEnding.Cr => "\r",
        LineEnding.Lf => "\n",
        LineEnding.CrLf => "\r\n",
        _ => throw new ArgumentOutOfRangeException(nameof(lineEnding), lineEnding, message: null),
    };
}
