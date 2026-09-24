using System.Buffers;
using DevTerm.Core.Presenters;

namespace DevTerm.Devices.Busylight;

/// <summary>
/// Decodes the Kuando Busylight's poll reply, which is an ASCII device-identification string (e.g.
/// "0001PLENOM0000010000000 1DASAN0002011081700...", confirmed live against real hardware — see
/// docs/design/features/kuando-busylight-protocol.md) rather than structured per-field telemetry.
/// No <see cref="IStructuredPresenter"/> companion — <see cref="BusylightUiDefinition"/> has no
/// <c>IndicatorControl</c>s for this to drive.
/// </summary>
public sealed class BusylightDecoder : IPresenter
{
    public string Name => "busylight";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        var bytes = data.ToArray();
        var text = new string(Array.ConvertAll(bytes, b => b is >= 0x20 and < 0x7F ? (char)b : '.'));
        return [$"BUSYLIGHT: {text}"];
    }
}
