using System.Buffers;
using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

public sealed class DecimalPresenter : IPresenter, IPresenterInput
{
    public string Name => "decimal";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) => [NumericBasePresenter.Render(data, toBase: 10, width: 1)];

    public byte[] Parse(string input) => NumericBasePresenter.Parse(input, fromBase: 10);
}
