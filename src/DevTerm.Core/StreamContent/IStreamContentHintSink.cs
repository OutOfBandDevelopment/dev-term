namespace DevTerm.Core.StreamContent;

/// <summary>
/// Something in a session's presenter pipeline that wants to know, ahead of time, what the next
/// reply is expected to contain — the "declared hint" detection path of
/// docs/design/proposals/stream-content-detection.md. A sender that knows (e.g. a SCPI command
/// declared with <c>ExpectedResponseFormat</c>) looks for this on the session's
/// <c>Presenters</c> and calls <see cref="ExpectResponse"/> just before sending, so nothing has to
/// be wired between the sender and whatever is watching — it simply isn't found when no Stream
/// Monitor is running.
/// </summary>
public interface IStreamContentHintSink
{
    /// <summary>The next reply is expected to be <paramref name="format"/>; <see cref="StreamContentFormat.Text"/> is ignored.</summary>
    void ExpectResponse(StreamContentFormat format);
}
