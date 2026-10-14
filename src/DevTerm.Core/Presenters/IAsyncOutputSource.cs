namespace DevTerm.Core.Presenters;

/// <summary>
/// A presenter whose <see cref="IPresenter.Render"/> returns at once and delivers its decoded lines later, when its
/// work (a child process answering) finishes. <see cref="Pipeline"/> forwards <see cref="OutputReady"/> to the
/// session's output, so the raw bytes are shown immediately by the other presenters and this one's lines follow,
/// possibly out of order with them.
/// </summary>
public interface IAsyncOutputSource
{
    /// <summary>Raised on a background thread with one or more rendered lines.</summary>
    event EventHandler<IReadOnlyList<string>>? OutputReady;
}
