using System.Buffers;
using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Test.Utilities;

/// <summary>
/// Emits whatever bytes arrived in a single read verbatim, with no line-terminator buffering —
/// unlike <see cref="DevTerm.Presenters.Text.AsciiPresenter"/>, this correctly surfaces a
/// terminatorless reply (e.g. a Korad supply's "05.00" for VOUT1?, or a Rigol DS1102E's USBTMC
/// reply, neither of which carries a CR/LF at all) as well as a line-terminated reply, since a
/// real-hardware test built on this only needs "some bytes came back" rather than exact line
/// framing. Shared by <c>DevTerm.Console.Tests.RealHardwareSerialTests</c>/
/// <c>RealHardwareUsbtmcTests</c>.
/// </summary>
public sealed class RawPresenter : IPresenter
{
    public string Name => "raw";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) =>
        data.IsEmpty ? [] : [Encoding.ASCII.GetString(BuffersExtensions.ToArray(data))];
}
