namespace DevTerm.Core.StreamContent;

/// <summary>Why a <see cref="StreamCapture"/> ended.</summary>
public enum StreamCaptureEnd
{
    /// <summary>The content's own structure (or a block's declared length) said it was complete.</summary>
    Complete,

    /// <summary>No more bytes arrived for <see cref="StreamContentWatcherOptions.IdleTimeout"/> — the usual end for formats with no in-band end marker (HP-GL, TIFF).</summary>
    IdleTimeout,

    /// <summary>The capture reached <see cref="StreamContentWatcherOptions.MaxCaptureBytes"/> and was cut off there.</summary>
    SizeLimit,

    /// <summary>The watcher was flushed (detached or stopped) while the capture was still in progress.</summary>
    Flushed,
}

/// <summary>One detected, captured piece of renderable/binary content from a session's incoming bytes.</summary>
/// <param name="Kind">What it was detected (or declared) as.</param>
/// <param name="Data">The captured bytes — the content itself, with any SCPI definite-length block header already stripped.</param>
/// <param name="StartedAt">When the first byte of it arrived.</param>
/// <param name="EndReason">Why the capture ended; anything but <see cref="StreamCaptureEnd.Complete"/> may mean it's truncated.</param>
/// <param name="WasDeclared">Whether a declared response-format hint (see <see cref="IStreamContentHintSink"/>) identified it, rather than sniffing alone.</param>
public sealed record StreamCapture(StreamContentKind Kind, byte[] Data, DateTimeOffset StartedAt, StreamCaptureEnd EndReason, bool WasDeclared);
