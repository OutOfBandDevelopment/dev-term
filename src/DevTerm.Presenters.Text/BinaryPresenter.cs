using System.Buffers;
using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

public sealed class BinaryPresenter : IPresenter, IPresenterInput
{
    public string Name => "binary";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) => [NumericBasePresenter.Render(data, toBase: 2, width: 8)];

    public byte[] Parse(string input) => NumericBasePresenter.Parse(input, fromBase: 2);
}
