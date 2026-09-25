namespace DevTerm.Core.Presenters;

/// <summary>
/// Optional companion to <see cref="IPresenter"/> for presenters that can also turn
/// user-facing input back into bytes to send.
/// </summary>
public interface IPresenterInput
{
    byte[] Parse(string input);
}
