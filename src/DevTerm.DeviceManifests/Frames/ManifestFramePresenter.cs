using System.Buffers;
using DevTerm.Core.Presenters;

namespace DevTerm.DeviceManifests;

/// <summary>
/// Finds <see cref="FrameSchema"/> frames in a binary byte stream and publishes each one's fields as live values
/// (<see cref="IStructuredPresenter.ValuesChanged"/>). Renders no text of its own, like <see cref="ManifestReplyPresenter"/>:
/// the connection's own presenters already show the bytes. Garbage before a frame is skipped by searching for the sync
/// bytes (or, with none declared, by dropping one byte whenever an expected constant doesn't match).
/// </summary>
public sealed class ManifestFramePresenter : IPresenter, IStructuredPresenter
{
    // A wedged stream that never completes a frame must not grow this without bound.
    private const int _maxBuffered = 64 * 1024;

    private readonly FrameDecoder _decoder;
    private readonly List<byte> _buffer = [];

    public ManifestFramePresenter(FrameSchema schema)
    {
        _decoder = new FrameDecoder(schema);
    }

    public string Name => "manifest-frame";

    public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        foreach (var segment in data)
        {
            _buffer.AddRange(segment.Span);
        }

        var sync = _decoder.Sync;
        var frames = new List<Dictionary<string, string>>();
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer);
        var at = 0;

        while (span.Length - at >= _decoder.Length)
        {
            if (sync.Length > 0)
            {
                var found = span[at..].IndexOf(sync);
                if (found < 0)
                {
                    // Keep a possible partial sync at the very end.
                    at = Math.Max(at, span.Length - (sync.Length - 1));
                    break;
                }

                at += found;
                if (span.Length - at < _decoder.Length)
                {
                    break;
                }
            }

            var decoded = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_decoder.TryDecode(span.Slice(at, _decoder.Length), decoded))
            {
                frames.Add(decoded);
                at += _decoder.Length;
            }
            else
            {
                at++;
            }
        }

        if (at > 0)
        {
            _buffer.RemoveRange(0, at);
        }

        if (_buffer.Count > _maxBuffered)
        {
            _buffer.Clear();
        }

        // One publish per frame, so a chart sees every sample rather than only the newest of a burst.
        foreach (var frame in frames)
        {
            ValuesChanged?.Invoke(this, frame);
        }

        return [];
    }
}
