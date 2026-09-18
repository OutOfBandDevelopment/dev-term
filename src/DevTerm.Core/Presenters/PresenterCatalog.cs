namespace DevTerm.Core.Presenters;

/// <summary>
/// Looks up the presenters contributed by all installed presenter plugins by name,
/// so a front end can resolve "hex" or "ascii" without knowing which plugin registered it.
/// </summary>
public sealed class PresenterCatalog
{
    private readonly IReadOnlyDictionary<string, IPresenter> _byName;

    public PresenterCatalog(IEnumerable<IPresenter> presenters)
    {
        ArgumentNullException.ThrowIfNull(presenters);
        _byName = presenters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)_byName.Keys;

    public IPresenter Get(string name)
    {
        if (TryGet(name, out var presenter))
        {
            return presenter;
        }

        throw new KeyNotFoundException($"No presenter named '{name}' is registered.");
    }

    public bool TryGet(string name, out IPresenter presenter) => _byName.TryGetValue(name, out presenter!);

    /// <summary>
    /// The names of every presenter that can also turn typed text into bytes
    /// (<see cref="IPresenterInput"/>) — what a front end offers as a "send format" (parser) choice.
    /// </summary>
    public IReadOnlyList<string> InputNames =>
        [.. _byName.Values.Where(p => p is IPresenterInput).Select(p => p.Name)];

    /// <summary>Resolves <paramref name="name"/> to a presenter that can encode typed input, or <see langword="false"/> if it's unknown or display-only.</summary>
    public bool TryGetInput(string name, out IPresenterInput input)
    {
        if (_byName.TryGetValue(name, out var presenter) && presenter is IPresenterInput found)
        {
            input = found;
            return true;
        }

        input = null!;
        return false;
    }
}
