using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

public sealed class HexPresenter : IPresenter, IPresenterInput
{
    public string Name => "hex";

    public string Render(ReadOnlyMemory<byte> data) => Convert.ToHexString(data.Span);

    public byte[] Parse(string input)
    {
        var cleaned = input.Replace(" ", string.Empty).Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        return Convert.FromHexString(cleaned);
    }
}
