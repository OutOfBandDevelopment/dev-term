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

    private static string ModeButtonText(StreamCaptureConverterOptions options)
    {
        var choices = StreamConversionChoice.For(options);
        return $"Convert as: {choices[StreamConversionChoice.IndexOf(choices, options)].DisplayName}";
    }

    internal static StreamMonitorWindowParts BuildWindow(IApplication app, StreamMonitor monitor, CliOptions? cliOptions = null, IEnumerable<StreamConvertToolOptions>? globalTools = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(monitor);
        monitor.LoadFromDisk();

        var converterOptions = StreamCaptureConverterOptions.FromCliOptions(cliOptions, globalTools);
        var converter = new StreamCaptureConverter(Microsoft.Extensions.Options.Options.Create(converterOptions));

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
        var folderLabel = new Label { X = 0, Y = 1, Width = Dim.Fill(), Height = 1, HotKeySpecifier = noHotKey };
        var explanationLabel = new Label { X = 0, Y = 2, Width = Dim.Fill(), Height = 2, Text = ExplanationText };
        var capturesLabel = new Label { X = 0, Y = 5, Text = "Search:" };
        var searchField = new TextField { X = Pos.Right(capturesLabel) + 1, Y = 5, Width = 24 };
        var sortButton = new Button { X = Pos.Right(searchField) + 2, Y = 5, Text = SortText(StreamCaptureSort.Oldest), ShadowStyle = ShadowStyles.None };
        var captureList = new ListView
        {
            X = 0,
            Y = 7,
            Width = Dim.Fill(),
            Height = Dim.Fill(4),
        };
        var detailLabel = new Label { X = 0, Y = Pos.Bottom(captureList), Width = Dim.Fill(), Height = 2, HotKeySpecifier = noHotKey };
        var toggleButton = new Button { X = 0, Y = Pos.Bottom(detailLabel), Text = "Stop Monitoring" };
        var modeButton = new Button { X = Pos.Right(toggleButton) + 2, Y = Pos.Top(toggleButton), Text = ModeButtonText(converterOptions) };
        var convertButton = new Button { X = Pos.Right(modeButton) + 2, Y = Pos.Top(toggleButton), Text = "Convert..." };
        var closeButton = new Button { X = Pos.Right(convertButton) + 2, Y = Pos.Top(toggleButton), Text = "Close", IsDefault = true };

        var rows = new ObservableCollection<string>();
        captureList.SetSource(rows);

        // The captures the list currently shows (StreamCaptureView over the monitor's own list), in the order shown.
        IReadOnlyList<StreamMonitorCapture> shown = [];
        var sort = StreamCaptureSort.Oldest;

        StreamMonitorCapture? Selected()
        {
            if (shown.Count == 0)
            {
                return null;
            }

            return captureList.SelectedItem is int selected && selected >= 0 && selected < shown.Count ? shown[selected] : shown.MaxBy(c => c.LocalStartedAt);
        }

        void ShowDetail()
        {
            if (Selected() is not { } capture)
            {
                detailLabel.Text = monitor.Captures.Count == 0 ? "Nothing captured yet." : "No capture matches the search.";
                return;
            }

            detailLabel.Text = Detail(capture);
        }

        // Rebuilds everything from the monitor's own state - called on the UI thread, directly while
        // building and via app.Invoke when the monitor reports a capture or a state change.
        void Refresh()
        {
            var running = monitor.IsRunning;
            statusLabel.Text = running
                ? $" ● Monitoring {monitor.DeviceName}"
                : $" ○ Stopped — {monitor.DeviceName}";
            var theme = ActiveTheme.Current;
            statusLabel.SetScheme(TuiTheme.Solid(running
                ? TuiTheme.Attribute(theme, ThemeRole.StatusConnectedText, ThemeRole.StatusConnected)
                : TuiTheme.Attribute(theme, ThemeRole.MenuForeground, ThemeRole.MenuBackground)));
            // One line, the folder's middle elided if need be: a long path used to wrap onto the
            // explanation below it.
            var otherFolders = monitor.ExportDirectories.Count - 1;
            var more = otherFolders > 0 ? $" (+{otherFolders} more)" : string.Empty;
            folderLabel.Text = $"Saving to: {TuiText.CompactPath(monitor.ExportDirectory, Math.Max((app.Screen.Width > 0 ? app.Screen.Width : 80) - 14 - more.Length, 20))}{more}";
            toggleButton.Text = running ? "Stop Monitoring" : "Start Monitoring";

            shown = StreamCaptureView.Apply(monitor.Captures, searchField.Text, sort: sort);
            rows.Clear();
            foreach (var capture in shown)
            {
                rows.Add(Row(capture));
            }

            if (shown.Count > 0)
            {
                captureList.SelectedItem = shown.Select((c, i) => (c, i)).MaxBy(x => (x.c.LocalStartedAt, x.i)).i;
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
        searchField.TextChanged += (_, _) => Refresh();
        sortButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            var all = Enum.GetValues<StreamCaptureSort>();
            sort = all[(Array.IndexOf(all, sort) + 1) % all.Length];
            sortButton.Text = SortText(sort);
            Refresh();
        };

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

        modeButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            var choices = StreamConversionChoice.For(converterOptions);
            var items = choices.Select(c => c.DisplayName).ToList();
            if (FormRenderer.PickFromList(app, "Conversion", items) is { } chosen)
            {
                choices[chosen].ApplyTo(converterOptions);
                modeButton.Text = ModeButtonText(converterOptions);
            }
        };

        convertButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (Selected() is not { } capture)
            {
                return;
            }

            detailLabel.Text = "Converting...";

            _ = converter.ConvertAsync(capture).ContinueWith(
                t => app.Invoke(() =>
                {
                    if (t.Result is { Success: true, OutputPath: { } output })
                    {
                        // Adds the file to the list and selects it; the line below then says what happened.
                        monitor.AddConverted(capture, output);
                        Refresh();
                    }

                    detailLabel.Text = t.Result.Success
                        ? $"Converted to {Path.GetFileName(t.Result.OutputPath)}."
                        : $"Convert failed: {t.Result.Error}";
                }),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        };

        closeButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        window.Add(statusLabel, folderLabel, explanationLabel, capturesLabel, searchField, sortButton, captureList, detailLabel, toggleButton, modeButton, convertButton, closeButton);
        Refresh();

        return new StreamMonitorWindowParts(window, statusLabel, captureList, detailLabel, toggleButton, convertButton, closeButton, Refresh, modeButton, converterOptions, searchField, sortButton);
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

    /// <summary>The sort button's label; pressing it cycles to the next order.</summary>
    internal static string SortText(StreamCaptureSort sort) => $"Sort: {sort}";

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
internal sealed record StreamMonitorWindowParts(Window Window, Label StatusLabel, ListView CaptureList, Label DetailLabel, Button ToggleButton, Button ConvertButton, Button CloseButton, Action Refresh, Button ModeButton, StreamCaptureConverterOptions ConverterOptions, TextField SearchField, Button SortButton);
