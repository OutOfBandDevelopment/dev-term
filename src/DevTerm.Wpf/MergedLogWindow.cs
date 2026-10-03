using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DevTerm.Configuration;

namespace DevTerm.Wpf;

/// <summary>
/// The merged, time-ordered traffic of every open tab (<see cref="MergedSessionLog"/>): one line per received or
/// sent chunk, tagged with its device. Follows the log live while open; Copy and Clear act on the shown text.
/// </summary>
internal sealed class MergedLogWindow : Window
{
    private readonly MergedSessionLog _log;
    private readonly TextBox _text;

    public MergedLogWindow(MergedSessionLog log)
    {
        _log = log;
        Title = "dev-term - All sessions log";
        Width = 760;
        Height = 480;
        MinWidth = 420;
        MinHeight = 240;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _text = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var copy = new Button { Content = "Copy", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => TryCopy();
        var clear = new Button { Content = "Clear", Padding = new Thickness(12, 2, 12, 2) };
        clear.Click += (_, _) =>
        {
            _log.Clear();
            _text.Clear();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { copy, clear } };
        var note = new TextBlock
        {
            Text = "Every tab's traffic in arrival order: > received, < sent. Recording starts when this window first opens.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var grid = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(buttons, Dock.Top);
        DockPanel.SetDock(note, Dock.Bottom);
        grid.Children.Add(buttons);
        grid.Children.Add(note);
        grid.Children.Add(_text);
        Content = grid;

        _text.Text = Render(_log.Entries);
        _text.ScrollToEnd();
        _log.EntryAdded += OnEntryAdded;
        Closed += (_, _) => _log.EntryAdded -= OnEntryAdded;
        WpfTheme.Attach(this);
    }

    internal string Text => _text.Text;

    internal static string Render(IEnumerable<MergedLogEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.Append(entry).Append('\n');
        }

        return builder.ToString();
    }

    private void OnEntryAdded(object? sender, MergedLogEntry entry) =>
        Dispatcher.BeginInvoke(() =>
        {
            _text.AppendText(entry + "\n");
            _text.ScrollToEnd();
        });

    private void TryCopy()
    {
        try
        {
            Clipboard.SetText(_text.Text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard briefly owned by another process.
        }
    }
}
