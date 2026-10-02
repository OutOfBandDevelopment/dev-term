using System.Buffers;
using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Presenters.Text;

/// <summary>
/// Decodes UTF-8 statefully across chunks via a per-instance <see cref="Decoder"/> - a multi-byte
/// character can arrive split across two transport reads (serial routinely delivers one byte per
/// read, and TCP can split anywhere). See docs/bugs/resolved/028-utf8-split-across-reads.md. Safe to
/// keep as instance state since presenters are resolved fresh per session (see CLAUDE.md).
/// </summary>
public sealed class Utf8Presenter : IPresenter, IPresenterInput
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public string Name => "utf8";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        var bytes = data.ToContiguousSpan();
        if (bytes.Length == 0)
        {
            return [];
        }

        var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
        var charCount = _decoder.GetChars(bytes, chars, flush: false);
        return charCount == 0 ? [] : [new string(chars, 0, charCount)];
    }

    public byte[] Parse(string input) => Encoding.UTF8.GetBytes(input);
}
