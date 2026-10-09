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
internal sealed class SessionPanels(IEnumerable<IDevicePanelContribution> contributions)
{
    private readonly IReadOnlyList<IDevicePanelContribution> _contributions = [.. contributions];
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

    /// <summary>The panels that make sense for the session's connection, in plugin order.</summary>
    public IReadOnlyList<(string Id, string Title)> For(SessionHub hub)
    {
        var options = hub.Options;
        return [.. _contributions
            .Where(c => c.IsAvailable(options.Transport, options.VendorId, options.ProductId))
            .Select(c => (c.Id, c.MenuTitle.Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('.')))];
    }

    /// <summary>The opened panel for the session (created on first use), or <see langword="null"/> for an unknown id.</summary>
    public OpenPanel? Open(SessionHub hub, string id)
    {
        var contribution = _contributions.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (contribution is null)
        {
            return null;
        }

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
