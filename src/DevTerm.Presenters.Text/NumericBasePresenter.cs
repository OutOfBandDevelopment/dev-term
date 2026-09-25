using System.Buffers;
using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

/// <summary>
/// Shared rendering for the fixed-width, space-separated numeric-base presenters
/// (decimal, octal, binary). Hex uses <see cref="Convert.ToHexString(byte[])"/> instead,
/// since it has its own conventional unspaced form.
/// </summary>
internal static class NumericBasePresenter
{
    public static string Render(ReadOnlySequence<byte> data, int toBase, int width)
    {
        var span = data.ToContiguousSpan();
        var sb = new StringBuilder();
        for (var i = 0; i < span.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(Convert.ToString(span[i], toBase).PadLeft(width, '0'));
        }

        return sb.ToString();
    }

    public static byte[] Parse(string input, int fromBase) =>
        [.. input
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => Convert.ToByte(token, fromBase))];
}
