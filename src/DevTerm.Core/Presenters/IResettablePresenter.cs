namespace DevTerm.Core.Presenters;

/// <summary>
/// A presenter that carries state across <see cref="IPresenter.Render"/> calls — a partial line, a
/// pending reply queue — and needs it cleared when the session it's bound to reopens, so state left
/// over from a previous connection (a stale pending reply id, a partial line from a mid-read
/// disconnect) doesn't bleed into the new one. <see cref="Sessions.Session.OpenAsync"/> calls
/// <see cref="Reset"/> on every bound presenter that implements this, before starting the read loop.
/// See docs/bugs/fixed/006-reply-queue-desync.md.
/// </summary>
public interface IResettablePresenter
{
    void Reset();
}
