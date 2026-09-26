using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevTerm.Configuration;
using Microsoft.Win32;

namespace DevTerm.Wpf;

/// <summary>One row of <see cref="StreamMonitorWindow.CaptureList"/>.</summary>
public sealed class StreamMonitorCaptureItem
{
    public StreamMonitorCaptureItem(StreamMonitorCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Capture = capture;
    }

    public StreamMonitorCapture Capture { get; }

    /// <summary>e.g. <c>14:35:12 — PNG image</c>.</summary>
    public string Title => $"{Capture.LocalStartedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} — {Capture.Capture.Kind.DisplayName}";

    /// <summary>e.g. <c>4,213 bytes · complete · scope_20260925-143512.png</c>.</summary>
    public string Subtitle
    {
        get
        {
            var size = Capture.Capture.Data.Length.ToString("N0", CultureInfo.InvariantCulture);
            var file = Capture.SavedPath is { } path ? Path.GetFileName(path) : "not saved";
            return $"{size} bytes · {Capture.EndLabel} · {file}";
        }
    }
}

/// <summary>
/// The WPF Stream Monitor window (Device > Stream Monitor...; docs/specs/stream-monitor.md): the
/// shared <see cref="StreamMonitor"/>'s state and export folder, a live list of its captures, and
/// — WPF's addition over the TUI — a live preview of the selected capture for the raster formats
/// WPF decodes natively (BMP/PNG/JPEG/GIF/TIFF), plus Export As... to save a copy somewhere other
/// than the automatic location. HP-GL/PostScript/PCL are captured and saved but not previewed yet
/// (that needs the rendering presenter from presenters.md §3 — the proposal's phase 2).
/// </summary>
/// <remarks>
/// Closing this window doesn't stop monitoring (the Stop button does) — the same as the TUI,
/// where the window is modal and monitoring has to carry on after it closes to be useful at all.
/// </remarks>
public partial class StreamMonitorWindow : Window
{
    private readonly StreamMonitor _monitor;

    public StreamMonitorWindow(StreamMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        InitializeComponent();
        WpfTheme.Attach(this);
        _monitor = monitor;

        CaptureList.ItemsSource = Items;
        foreach (var capture in monitor.Captures)
        {
            Items.Add(new StreamMonitorCaptureItem(capture));
        }

        monitor.CaptureAdded += OnCaptureAdded;
        monitor.StateChanged += OnStateChanged;
        Closed += (_, _) =>
        {
            monitor.CaptureAdded -= OnCaptureAdded;
            monitor.StateChanged -= OnStateChanged;
        };

        RefreshState();
        SelectNewest();
    }

    /// <summary>The capture list's rows, oldest first.</summary>
    internal ObservableCollection<StreamMonitorCaptureItem> Items { get; } = [];

    /// <summary>
    /// Decodes <paramref name="data"/> with WPF's built-in codecs (BMP, PNG, JPEG, GIF, TIFF, ICO,
    /// WDP), or returns <see langword="null"/> with the decoder's reason — a truncated capture (one
    /// that ended on idle or was cut off) often won't decode.
    /// </summary>
    internal static BitmapSource? TryDecode(byte[] data, out string? error)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            error = null;
            return frame;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException or InvalidOperationException or OverflowException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Shows <paramref name="item"/> (or nothing) in the preview/detail area.</summary>
    internal void ShowCapture(StreamMonitorCaptureItem? item)
    {
        PreviewImage.Source = null;
        ExportAsButton.IsEnabled = item is not null;

        if (item is null)
        {
            PreviewMessage.Text = "Nothing captured yet. Send a command that returns an image, plot or print job — or wait for the device to send one.";
            DetailText.Text = string.Empty;
            SavedPathText.Text = string.Empty;
            return;
        }

        var capture = item.Capture;
        DetailText.Text = capture.Summary;
        SavedPathText.Text = capture.SavedPath is { } path ? $"Saved to {StreamMonitor.DisplayPath(path)}" : $"Not saved: {capture.SaveError}";
        SavedPathText.ToolTip = capture.SavedPath;

        if (!capture.Capture.Kind.IsNativeImage)
        {
            PreviewMessage.Text = $"Preview not available yet for {capture.Capture.Kind.DisplayName} — the captured bytes were saved as-is.";
            return;
        }

        if (TryDecode(capture.Capture.Data, out var error) is { } image)
        {
            PreviewImage.Source = image;
            PreviewMessage.Text = string.Empty;
        }
        else
        {
            PreviewMessage.Text = $"Could not preview this {capture.Capture.Kind.DisplayName}: {error}";
        }
    }

    private void OnCaptureAdded(object? sender, StreamMonitorCapture capture) =>
        Dispatcher.BeginInvoke(() =>
        {
            Items.Add(new StreamMonitorCaptureItem(capture));
            while (Items.Count > StreamMonitor.MaxRetainedCaptures)
            {
                Items.RemoveAt(0);
            }

            SelectNewest();
        });

    private void OnStateChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshState);

    /// <summary>The state line, folder line and toggle label, from the monitor's own state.</summary>
    internal void RefreshState()
    {
        var running = _monitor.IsRunning;
        StateText.Text = running ? $"Monitoring {_monitor.DeviceName}" : $"Stopped — {_monitor.DeviceName}";
        StateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, WpfTheme.Key(running ? ThemeRole.StatusConnected : ThemeRole.MutedForeground));
        FolderText.Text = $"Saving to {StreamMonitor.DisplayPath(_monitor.ExportDirectory)}";
        FolderText.ToolTip = _monitor.ExportDirectory;
        ToggleButton.Content = running ? "Stop Monitoring" : "Start Monitoring";
    }

    private void SelectNewest()
    {
        if (Items.Count == 0)
        {
            ShowCapture(null);
            return;
        }

        CaptureList.SelectedIndex = Items.Count - 1;
        CaptureList.ScrollIntoView(Items[^1]);
        ShowCapture(Items[^1]);
    }

    private void CaptureList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowCapture(CaptureList.SelectedItem as StreamMonitorCaptureItem);

    /// <summary>The Start/Stop button's action — exposed so tests can drive it without a click.</summary>
    internal void ToggleMonitoring()
    {
        if (_monitor.IsRunning)
        {
            _monitor.Stop();
        }
        else
        {
            _monitor.Start();
        }

        RefreshState();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e) => ToggleMonitoring();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_monitor.ExportDirectory);
            Process.Start(new ProcessStartInfo { FileName = _monitor.ExportDirectory, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Could not open '{_monitor.ExportDirectory}': {ex.Message}", "dev-term — Stream Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportAs_Click(object sender, RoutedEventArgs e)
    {
        if (CaptureList.SelectedItem is not StreamMonitorCaptureItem item)
        {
            return;
        }

        var capture = item.Capture;
        var extension = capture.Capture.Kind.Extension;
        var dialog = new SaveFileDialog
        {
            FileName = capture.SavedPath is { } saved ? Path.GetFileName(saved) : $"capture.{extension}",
            DefaultExt = "." + extension,
            Filter = $"{capture.Capture.Kind.DisplayName} (*.{extension})|*.{extension}|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, capture.Capture.Data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save '{dialog.FileName}': {ex.Message}", "dev-term — Stream Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
