namespace DevTerm.Core.Presenters;

/// <summary>
/// A presenter that carves non-text content (an image, a plot, a binary block) out of the byte stream.
/// <see cref="Pipeline"/> withholds a chunk it consumed from every other presenter, so a line-buffering text
/// presenter doesn't print the content as garbage and keep its tail to prepend to the next reply.
/// </summary>
public interface IContentCapturePresenter : IPresenter
{
    /// <summary>Whether a capture is currently in progress.</summary>
    bool IsCapturing { get; }

    /// <summary>How many captures have finished, ever; a change across one <c>Render</c> means a capture ended in it.</summary>
    long CompletedCount { get; }
}
