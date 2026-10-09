using System.Collections.Concurrent;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.UiDefinitions;

namespace DevTerm.Web;

/// <summary>
/// The Device menu for one web session: the plugin-contributed panels that suit its connection, each opened on demand as a
/// <see cref="UiDefinition"/> plus a live <see cref="IControlSurface"/> and the latest indicator values. A profile switch
/// replaces the session, so opened panels are dropped and rebuilt against the new one on next use.
/// </summary>
internal sealed class SessionPanels(IEnumerable<IDevicePanelContribution> contributions, IEnumerable<IInstrumentPanelProvider>? instruments = null)
{
    private const string _instrumentPrefix = "i~";
    private readonly IReadOnlyList<IDevicePanelContribution> _contributions = [.. contributions];
    private readonly IReadOnlyList<IInstrumentPanelProvider> _instruments = [.. instruments ?? []];
    private readonly ConcurrentDictionary<(SessionHub Hub, string Id), OpenPanel> _open = new();
    private readonly ConcurrentDictionary<SessionHub, bool> _watched = new();

    /// <summary>One opened panel: its form, the surface that drives the device and the latest value per indicator id.</summary>
    internal sealed class OpenPanel(UiDefinition definition, IControlSurface surface)
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

        public UiDefinition Definition { get; } = definition;

        public IControlSurface Surface { get; } = surface;

        public IReadOnlyDictionary<string, string> Values => _values;

        public void Merge(IReadOnlyDictionary<string, string> values)
        {
            foreach (var (id, value) in values)
            {
                _values[id] = value;
            }
        }
    }

    /// <summary>The panels that make sense for the session's connection, in plugin order: fixed-device panels, then each instrument family's choices.</summary>
    public IReadOnlyList<(string Id, string Title)> For(SessionHub hub)
    {
        var options = hub.Options;
        var panels = _contributions
            .Where(c => c.IsAvailable(options.Transport, options.VendorId, options.ProductId))
            .Select(c => (c.Id, c.MenuTitle.Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('.')))
            .ToList();
        foreach (var provider in _instruments.Where(p => p.IsAvailable(options.Transport, options.VendorId, options.ProductId)))
        {
            var title = provider.MenuTitle.Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('.');
            var choices = provider.PickerChoices();
            for (var i = 0; i < choices.Count; i++)
            {
                panels.Add(($"{_instrumentPrefix}{provider.Id}~{i}", $"{title}: {choices[i]}"));
            }
        }

        return panels;
    }

    /// <summary>
    /// The opened panel for an instrument choice (<c>i~provider~index</c> from <see cref="For"/>): the provider's structured presenter is
    /// bound into the live session, auto-detect asks the device which instrument it is, then the matched profile's panel opens.
    /// <see langword="null"/> for an unknown id.
    /// </summary>
    public async Task<OpenPanel?> OpenInstrumentAsync(SessionHub hub, string id)
    {
        var parts = id.Split('~');
        if (parts.Length != 3 || parts[0] + "~" != _instrumentPrefix || !int.TryParse(parts[2], out var index)
            || _instruments.FirstOrDefault(p => string.Equals(p.Id, parts[1], StringComparison.OrdinalIgnoreCase)) is not { } provider)
        {
            return null;
        }

        var choices = provider.PickerChoices();
        if (index < 0 || index >= choices.Count)
        {
            return null;
        }

        Watch(hub);
        if (_open.TryGetValue((hub, id), out var existing))
        {
            return existing;
        }

        IPresenter? structured = null;
        if (hub.Catalog.TryGet(provider.PresenterName, out var presenter))
        {
            hub.Session.AddPresenter(presenter);
            structured = presenter;
        }

        var choice = choices[index];
        if (choice == provider.AutoDetectChoice)
        {
            choice = (await provider.DetectAsync(hub.Session, structured, TimeSpan.FromMilliseconds(hub.Options.ScpiAutoDetectTimeoutMs))).Choice;
        }

        var instrument = provider.Open(choice, hub.Session, structured);
        hub.RecordInstrument(choice);
        var panel = new OpenPanel(instrument.Definition, instrument.Surface);
        if (structured is IStructuredPresenter source)
        {
            source.ValuesChanged += (_, values) => panel.Merge(values);
        }

        return _open.GetOrAdd((hub, id), panel);
    }

    private void Watch(SessionHub hub)
    {
        if (_watched.TryAdd(hub, true))
        {
            hub.SessionChanged += () =>
            {
                foreach (var key in _open.Keys.Where(k => ReferenceEquals(k.Hub, hub)))
                {
                    _open.TryRemove(key, out _);
                }
            };
        }
    }

    /// <summary>The opened panel for the session (created on first use), or <see langword="null"/> for an unknown id.</summary>
    public OpenPanel? Open(SessionHub hub, string id)
    {
        var contribution = _contributions.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (contribution is null)
        {
            return null;
        }

        Watch(hub);

        return _open.GetOrAdd((hub, contribution.Id), _ =>
        {
            var panel = new OpenPanel(contribution.BuildDefinition(), contribution.CreateSurface(hub.Session));
            if (contribution.PresenterName is { } name && hub.Catalog.TryGet(name, out var presenter) && presenter is IStructuredPresenter structured)
            {
                structured.ValuesChanged += (_, values) => panel.Merge(values);
            }

            return panel;
        });
    }
}
