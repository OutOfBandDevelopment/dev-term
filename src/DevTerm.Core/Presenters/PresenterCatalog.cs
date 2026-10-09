namespace DevTerm.Core.Presenters;

/// <summary>
/// Looks up the presenters contributed by all installed presenter plugins by name,
/// so a front end can resolve "hex" or "ascii" without knowing which plugin registered it.
/// </summary>
public sealed class PresenterCatalog
{
    private readonly Dictionary<string, IPresenter> _byName;
    private readonly Lock _gate = new();

    public PresenterCatalog(IEnumerable<IPresenter> presenters)
    {
        ArgumentNullException.ThrowIfNull(presenters);
        _byName = presenters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every presenter name, including plugins approved while running (<see cref="Plugins.LivePlugins"/>).</summary>
    public IReadOnlyCollection<string> Names
    {
        get
        {
            Refresh();
            lock (_gate)
            {
                return [.. _byName.Keys];
            }
        }
    }

    /// <summary>Adds the presenters of plugins that went live since this catalog was built, without replacing any it already holds.</summary>
    private void Refresh()
    {
        foreach (var live in Plugins.LivePlugins.CreatePresenters())
        {
            lock (_gate)
            {
                _byName.TryAdd(live.Name, live);
            }
        }
    }

    public IPresenter Get(string name)
    {
        if (TryGet(name, out var presenter))
        {
            return presenter;
        }

        throw new KeyNotFoundException($"No presenter named '{name}' is registered.");
    }

    public bool TryGet(string name, out IPresenter presenter)
    {
        Refresh();
        lock (_gate)
        {
            return _byName.TryGetValue(name, out presenter!);
        }
    }

    /// <summary>
    /// The names of every presenter that can also turn typed text into bytes
    /// (<see cref="IPresenterInput"/>) — what a front end offers as a "send format" (parser) choice.
    /// </summary>
    public IReadOnlyList<string> InputNames =>
        [.. SnapshotValues().Where(p => p is IPresenterInput).Select(p => p.Name)];

    private List<IPresenter> SnapshotValues()
    {
        Refresh();
        lock (_gate)
        {
            return [.. _byName.Values];
        }
    }

    /// <summary>Resolves <paramref name="name"/> to a presenter that can encode typed input, or <see langword="false"/> if it's unknown or display-only.</summary>
    public bool TryGetInput(string name, out IPresenterInput input)
    {
        if (TryGet(name, out var presenter) && presenter is IPresenterInput found)
        {
            input = found;
            return true;
        }

        input = null!;
        return false;
    }
}
