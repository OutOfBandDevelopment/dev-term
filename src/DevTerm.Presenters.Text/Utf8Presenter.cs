using System.Buffers;
using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

public sealed class Utf8Presenter : IPresenter, IPresenterInput
{
    public string Name => "utf8";

    public string Render(ReadOnlySequence<byte> data) => Encoding.UTF8.GetString(data.ToContiguousSpan());

    public byte[] Parse(string input) => Encoding.UTF8.GetBytes(input);
}
