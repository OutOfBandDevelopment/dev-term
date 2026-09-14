using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

public sealed class OctalPresenter : IPresenter, IPresenterInput
{
    public string Name => "octal";

    public string Render(ReadOnlyMemory<byte> data) => NumericBasePresenter.Render(data, toBase: 8, width: 3);

    public byte[] Parse(string input) => NumericBasePresenter.Parse(input, fromBase: 8);
}
