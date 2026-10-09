using System.Windows;
using System.Windows.Controls;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;

namespace DevTerm.Wpf;

/// <summary>
/// Device-menu entries for instrument families a plugin provides (<see cref="IInstrumentPanelProvider"/>, SCPI today): one item
/// each, which asks which instrument (or auto-detects it) and opens that instrument's data-driven panel.
/// </summary>
public partial class MainWindow
{
    private readonly List<(IInstrumentPanelProvider Provider, MenuItem Item)> _instrumentItems = [];

    /// <summary>Set by <c>App</c> from the container; builds one Device-menu item per provider, after the plugin-contributed panels.</summary>
    public IReadOnlyList<IInstrumentPanelProvider>? InstrumentProviders
    {
        get => [.. _instrumentItems.Select(i => i.Provider)];
        set
        {
            foreach (var (_, item) in _instrumentItems)
            {
                ((MenuItem)item.Parent).Items.Remove(item);
            }

            _instrumentItems.Clear();
            var menu = (MenuItem)PluginsMenuItem.Parent;
            var position = _pluginPanelItems.Count;
            foreach (var provider in value ?? [])
            {
                var captured = provider;
                var item = new MenuItem { Header = captured.MenuTitle, IsEnabled = false };
                item.Click += (_, _) => OpenInstrument(captured);
                menu.Items.Insert(position++, item);
                _instrumentItems.Add((captured, item));
            }
        }
    }

    internal MenuItem? ScpiMenuItem => _instrumentItems.FirstOrDefault(i => i.Provider.Id == "scpi").Item;

    private void RefreshInstrumentItems(WindowTab tab, bool connected)
    {
        foreach (var (provider, item) in _instrumentItems)
        {
            item.IsEnabled = connected && provider.IsAvailable(tab.Tab.CliOptions.Transport, tab.Tab.CliOptions.VendorId, tab.Tab.CliOptions.ProductId);
        }
    }

    private void OpenInstrument(IInstrumentPanelProvider provider)
    {
        var tab = ActiveWindowTab;
        var chosen = provider.Offers(tab.Tab.CliOptions.ScpiProfile) ? tab.Tab.CliOptions.ScpiProfile : null;
        if (chosen is null)
        {
            var picker = new InstrumentPickerWindow(provider) { Owner = this };
            if (picker.ShowDialog() != true || picker.Chosen is not { } picked)
            {
                return;
            }

            chosen = picked;
        }

        var structuredSource = ResolveInstrumentPresenter(provider);
        if (chosen == provider.AutoDetectChoice)
        {
            Observe(DetectAndOpenInstrumentAsync(provider, structuredSource));
            return;
        }

        OpenInstrumentWindow(tab, provider, chosen, structuredSource);
    }

    /// <summary>
    /// Resolves the provider's presenter from the active tab's catalog and binds it into that tab's session's live pipeline if it
    /// isn't there already (see <c>TuiMode.ResolveInstrumentPresenter</c> for why the catalog lookup alone is not enough).
    /// </summary>
    private IPresenter? ResolveInstrumentPresenter(IInstrumentPanelProvider provider)
    {
        var tab = ActiveWindowTab;
        if (!tab.Tab.Catalog.TryGet(provider.PresenterName, out var presenter))
        {
            return null;
        }

        tab.Tab.Session.AddPresenter(presenter);
        return presenter;
    }

    // The detect query is a real send/await over the live transport, so unlike the synchronous picker this can't finish before the
    // click handler returns - fire-and-forget (observed). Reports progress while it waits (a wait cursor and a status line) and what
    // it found afterward, using the connection's configured timeout.
    /// <summary>internal so a test can drive the auto-detect-during-a-profile-switch race directly.</summary>
    internal async Task DetectAndOpenInstrumentAsync(IInstrumentPanelProvider provider, IPresenter? structuredSource)
    {
        if (ActiveWindowTabOrNull is not { } tab)
        {
            return;
        }

        // Captured so that if SwitchProfileAsync replaces this tab's Session/Catalog while this detection is in flight, the
        // completion below can tell and not open a panel pairing the NEW session with structuredSource from the OLD catalog - part
        // of bug 016, see docs/bugs/resolved/016-wpf-panels-bound-to-old-session.md's "Related" note.
        var sessionAtStart = tab.Tab.Session;
        var timeout = TimeSpan.FromMilliseconds(tab.Tab.CliOptions.ScpiAutoDetectTimeoutMs);
        AppendOutput(tab, provider.DetectProgressMessage(timeout), OutputKind.Status);

        InstrumentDetection detection;
        var previousCursor = Cursor;
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            detection = await provider.DetectAsync(sessionAtStart, structuredSource, timeout);
        }
        catch (Exception ex)
        {
            // The detect send failed - the session has disconnected itself and reported why, so there's no connection to open a panel against.
            AppendOutput(tab, $"Auto-detect failed: {ex.Message}", OutputKind.Error);
            return;
        }
        finally
        {
            Cursor = previousCursor;
        }

        if (!ReferenceEquals(tab.Tab.Session, sessionAtStart))
        {
            // The profile changed while auto-detect was waiting; the detected instrument belongs to a connection that's already
            // closed, so there's nothing live to open a panel against.
            return;
        }

        AppendOutput(tab, detection.Message, OutputKind.Status);
        OpenInstrumentWindow(tab, provider, detection.Choice, structuredSource);
    }

    // Show(), not ShowDialog(): this panel is meant to stay open and update live alongside the main window, not block it. Reuses the
    // current, already-open session rather than opening a second competing connection to the same physical device.
    private void OpenInstrumentWindow(WindowTab tab, IInstrumentPanelProvider provider, string choice, IPresenter? structuredSource)
    {
        var panel = provider.Open(choice, tab.Tab.Session, structuredSource);
        tab.Logger?.RecordInstrument(choice);
        var window = new ControlPanelWindow(panel.Definition, panel.Surface, structuredSource)
        {
            Owner = this,
        };
        TrackControlPanel(window, tab);
        window.Show();
    }
}
