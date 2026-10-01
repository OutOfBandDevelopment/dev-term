namespace DevTerm.Transports.Loopback;

/// <summary>
/// Configuration for <see cref="LoopbackTransport"/>. The transport always answers with
/// <see cref="LoopbackScript.Default"/> — this type exists so it fits the same DI shape as every
/// other transport and can grow a custom-script option later without a breaking constructor change.
/// </summary>
public sealed class LoopbackTransportOptions
{
    /// <summary>
    /// Milliseconds to wait between each pushed line of a naturally multi-line/streaming scripted
    /// response (<see cref="LoopbackRule.Streaming"/> — <c>Samples: N</c>, <c>Send Events: N</c>).
    /// Default <c>0</c> preserves the original instant-delivery behavior: every line is pushed back
    /// to back with no delay, unless a profile opts into pacing. A single-line response (<c>hello</c>,
    /// <c>MEAS?</c>, <c>help</c>, and <c>Send Stream: N, ascii</c>, which is one line regardless of
    /// <c>N</c>) is never delayed — there's nothing to pace within a single write.
    /// </summary>
    public int SampleIntervalMs { get; set; }
}
