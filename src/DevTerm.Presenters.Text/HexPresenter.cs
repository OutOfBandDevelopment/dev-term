using System.Buffers;
using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

public sealed class HexPresenter : IPresenter, IPresenterInput
{
    public string Name => "hex";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) => [Convert.ToHexString(data.ToContiguousSpan())];

    public byte[] Parse(string input)
    {
        var cleaned = input.Replace(" ", string.Empty).Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        return Convert.FromHexString(cleaned);
    }
}
