using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

public sealed class Utf8Presenter : IPresenter, IPresenterInput
{
    public string Name => "utf8";

    public string Render(ReadOnlyMemory<byte> data) => Encoding.UTF8.GetString(data.Span);

    public byte[] Parse(string input) => Encoding.UTF8.GetBytes(input);
}
