using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using DevTerm.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI's Stream Monitor window (Device > Stream Monitor...; docs/specs/stream-monitor.md): the
/// monitor's state, where captures are saved, and a live list of captures so far, with a
/// Start/Stop toggle. Terminal.Gui can't draw images, so there's no preview here by design — every
/// capture is auto-saved by the shared <see cref="StreamMonitor"/> the moment it completes, and the
/// main window's output gets a status line for it, so monitoring carries on usefully after this
/// (modal) window is closed and the user goes back to sending commands.
/// </summary>
internal static class StreamMonitorMode
{
    /// <summary>What the window shows about how detection works, and why there's no preview.</summary>
    internal const string ExplanationText =
        "Detects images (PNG, JPEG, GIF, BMP, TIFF), HP-GL, PostScript and PCL in the\n" +
        "incoming data and saves each automatically. No preview here - open the file.";

    internal static StreamMonitorWindowParts BuildWindow(IApplication app, StreamMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(monitor);

        var window = new Window
        {
            Title = "dev-term — Stream Monitor",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        // Device names and file paths are data, not menu text - never treat an '_' in one as a hotkey marker.
        var noHotKey = (Rune)0xFFFF;
        var statusLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), HotKeySpecifier = noHotKey };
        var folderLabel = new Label { X = 0, Y = 1, Width = Dim.Fill(), HotKeySpecifier = noHotKey };
        var explanationLabel = new Label { X = 0, Y = 2, Width = Dim.Fill(), Height = 2, Text = ExplanationText };
        var capturesLabel = new Label { X = 0, Y = 5, Text = "Captures (newest last):" };
        var captureList = new ListView
        {
            X = 0,
            Y = 6,
            Width = Dim.Fill(),
            Height = Dim.Fill(4),
        };
        var detailLabel = new Label { X = 0, Y = Pos.Bottom(captureList), Width = Dim.Fill(), Height = 2, HotKeySpecifier = noHotKey };
        var toggleButton = new Button { X = 0, Y = Pos.Bottom(detailLabel), Text = "Stop Monitoring" };
        var closeButton = new Button { X = Pos.Right(toggleButton) + 2, Y = Pos.Top(toggleButton), Text = "Close", IsDefault = true };

        var rows = new ObservableCollection<string>();
        captureList.SetSource(rows);

        void ShowDetail()
        {
            var captures = monitor.Captures;
            if (captures.Count == 0)
            {
                detailLabel.Text = "Nothing captured yet.";
                return;
            }

            var index = captureList.SelectedItem is int selected && selected >= 0 && selected < captures.Count ? selected : captures.Count - 1;
            detailLabel.Text = Detail(captures[index]);
        }

        // Rebuilds everything from the monitor's own state - called on the UI thread, directly while
        // building and via app.Invoke when the monitor reports a capture or a state change.
        void Refresh()
        {
            var running = monitor.IsRunning;
            statusLabel.Text = running
                ? $" ● Monitoring {monitor.DeviceName}"
                : $" ○ Stopped — {monitor.DeviceName}";
            var (foreground, background) = running
                ? (new Terminal.Gui.Drawing.Color(0, 0, 0, 255), new Terminal.Gui.Drawing.Color(120, 200, 120, 255))
                : (new Terminal.Gui.Drawing.Color(0, 0, 0, 255), new Terminal.Gui.Drawing.Color(200, 200, 200, 255));
            statusLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(foreground, background)));
            folderLabel.Text = $"Saving to: {StreamMonitor.DisplayPath(monitor.ExportDirectory)}";
            toggleButton.Text = running ? "Stop Monitoring" : "Start Monitoring";

            var captures = monitor.Captures;
            rows.Clear();
            foreach (var capture in captures)
            {
                rows.Add(Row(capture));
            }

            if (captures.Count > 0)
            {
                captureList.SelectedItem = captures.Count - 1;
            }

            ShowDetail();
        }

        void OnMonitorChanged(object? sender, EventArgs e) => app.Invoke(Refresh);
        void OnCaptureAdded(object? sender, StreamMonitorCapture e) => app.Invoke(Refresh);
        monitor.StateChanged += OnMonitorChanged;
        monitor.CaptureAdded += OnCaptureAdded;
        window.Disposing += (_, _) =>
        {
            monitor.StateChanged -= OnMonitorChanged;
            monitor.CaptureAdded -= OnCaptureAdded;
        };

        captureList.ValueChanged += (_, _) => ShowDetail();

        toggleButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (monitor.IsRunning)
            {
                monitor.Stop();
            }
            else
            {
                monitor.Start();
            }

            // Already on the UI thread (a button handler) - refresh directly rather than waiting
            // for the StateChanged → app.Invoke round trip, which never flushes under a headless test.
            Refresh();
        };

        closeButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        window.Add(statusLabel, folderLabel, explanationLabel, capturesLabel, captureList, detailLabel, toggleButton, closeButton);
        Refresh();

        return new StreamMonitorWindowParts(window, statusLabel, captureList, detailLabel, toggleButton, closeButton, Refresh);
    }

    /// <summary>
    /// One capture-list row, compact enough for an 80-column terminal: time, type (the saved
    /// file's extension), size, how it ended, and the saved file's name. The detail lines under the
    /// list spell out the full kind and path for the selected row.
    /// </summary>
    internal static string Row(StreamMonitorCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var time = capture.LocalStartedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var type = capture.Capture.Kind.Extension.ToUpperInvariant();
        var size = capture.Capture.Data.Length.ToString("N0", CultureInfo.InvariantCulture) + " B";
        var file = capture.SavedPath is { } path ? Path.GetFileName(path) : "NOT SAVED";
        return $"{time}  {type,-4} {size,11}  {capture.EndLabel,-10}  {file}";
    }

    /// <summary>The two detail lines under the list for the selected capture: what it is, then where it went.</summary>
    internal static string Detail(StreamMonitorCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var what = capture.Summary;
        return capture.SavedPath is { } path
            ? $"{what}\nSaved as {Path.GetFileName(path)} in the folder above."
            : $"{what}\nNot saved: {capture.SaveError}";
    }
}

/// <summary>The Stream Monitor window's controls, for tests to drive/inspect headlessly — <see cref="Refresh"/> is what the window runs whenever the monitor changes.</summary>
internal sealed record StreamMonitorWindowParts(Window Window, Label StatusLabel, ListView CaptureList, Label DetailLabel, Button ToggleButton, Button CloseButton, Action Refresh);
