using System.Buffers;
using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

public sealed class AsciiPresenter : IPresenter, IPresenterInput
{
    public string Name => "ascii";

    public string Render(ReadOnlySequence<byte> data) => Encoding.ASCII.GetString(data.ToContiguousSpan());

    public byte[] Parse(string input) => Encoding.ASCII.GetBytes(input);
}
