namespace DevTerm.Core.Presenters;

/// <summary>
/// The ordered set of presenters a session's raw byte stream is fanned out to.
/// See docs/design/architecture.md and docs/design/presenters.md.
/// </summary>
public sealed class Pipeline
{
    private readonly List<IPresenter> _presenters;

    public Pipeline(IEnumerable<IPresenter> presenters)
    {
        ArgumentNullException.ThrowIfNull(presenters);
        _presenters = [.. presenters];
    }

    public IReadOnlyList<IPresenter> Presenters => _presenters;

    public IReadOnlyList<PresenterOutput> Render(ReadOnlyMemory<byte> data)
    {
        var results = new List<PresenterOutput>(_presenters.Count);
        foreach (var presenter in _presenters)
        {
            results.Add(new PresenterOutput(presenter.Name, presenter.Render(data)));
        }

        return results;
    }
}
