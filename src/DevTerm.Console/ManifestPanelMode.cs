using System.Collections.ObjectModel;
using DevTerm.Configuration;
using DevTerm.Core.Sessions;
using DevTerm.DeviceManifests;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI's "Device > Device Manifest..." flow: pick a manifest (the user's and installed ones,
/// <see cref="InstalledManifests"/>, or any typed path to a manifest file, folder, or <c>.zip</c>),
/// load it with <see cref="DeviceManifestLoader"/>, and open its panel on the current session through
/// the generic <see cref="ControlPanelMode"/> renderer — the manifest's reply presenter bound into
/// the session only while the panel is open (<see cref="ManifestPanel"/>). A manifest that fails to
/// load throws, and the menu item's error guard shows why.
/// </summary>
internal static class ManifestPanelMode
{
    /// <summary>Picks, loads, and runs a manifest panel (a nested <c>Application.Run</c>); does nothing if the picker is cancelled.</summary>
    public static void PickAndRun(IApplication app, Session session)
    {
        if (Pick(app, InstalledManifests.Discover()) is not { } path)
        {
            return;
        }

        Run(app, session, DeviceManifestLoader.Load(path));
    }

    /// <summary>Opens <paramref name="manifest"/>'s panel on <paramref name="session"/> until it's closed.</summary>
    public static void Run(IApplication app, Session session, DeviceManifest manifest)
    {
        using var panel = ManifestPanel.Attach(session, manifest);
        var parts = BuildWindow(app, panel);
        app.Run(parts.Window);
    }

    /// <summary>The panel window for an attached <paramref name="panel"/> — split out so tests can drive it headlessly.</summary>
    public static ControlPanelWindowParts BuildWindow(IApplication app, ManifestPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return ControlPanelMode.BuildWindow(app, panel.Definition, panel.Surface, panel.Presenter, panel.Title);
    }

    /// <summary>
    /// A small modal picker: every discovered manifest, plus a path field for one anywhere else
    /// (a typed path wins over the list selection). Returns the chosen path, or null when cancelled.
    /// </summary>
    private static string? Pick(IApplication app, IReadOnlyList<ManifestEntry> entries)
    {
        string? picked = null;
        var dialog = new Dialog { Title = "Open Device Manifest", Width = 72, Height = Math.Clamp(entries.Count + 8, 10, 22) };
        var listView = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(4) };
        listView.SetSource(new ObservableCollection<string>(entries.Count > 0
            ? entries.Select(e => e.DisplayName)
            : [$"(no manifests in {DevTermUserDataPaths.UserManifestsDirectory})"]));
        var pathLabel = new Label { X = 0, Y = Pos.AnchorEnd(3), Text = "Or a path (file, folder, .zip):" };
        var pathField = new TextField { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill() };

        void Choose()
        {
            if (!string.IsNullOrWhiteSpace(pathField.Text))
            {
                picked = pathField.Text.Trim().Trim('"');
            }
            else if (listView.SelectedItem is int index && index >= 0 && index < entries.Count)
            {
                picked = entries[index].Path;
            }

            app.RequestStop();
        }

        listView.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        pathField.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        var openButton = new Button { X = 0, Y = Pos.AnchorEnd(1), Text = "Open", IsDefault = true };
        openButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        var cancelButton = new Button { X = Pos.Right(openButton) + 1, Y = Pos.AnchorEnd(1), Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };
        dialog.Add(listView, pathLabel, pathField, openButton, cancelButton);
        app.Run(dialog);
        return picked;
    }
}
