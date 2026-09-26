using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DevTerm.Logging;
using DevTerm.Logging.Playback;

namespace DevTerm.Wpf;

/// <summary>
/// WPF's Playback window (File &gt; Open Log for Playback...): replays a session log through a
/// chosen set of presenters with transport controls, a seek slider, trim and notes. All behavior is
/// the shared <see cref="PlaybackController"/> (the TUI's <c>PlaybackMode</c> draws the same one);
/// this only renders it. Never touches a transport. Non-modal, like the control panels. See
/// docs/specs/playback-window.md.
/// </summary>
public partial class PlaybackWindow : Window
{
    private const int _maxOutputLines = 1000;

    private readonly PlaybackController _controller;
    private readonly DispatcherTimer _timer;
    private readonly List<PresenterChoice> _choices;
    private bool _updating;

    public PlaybackWindow(PlaybackController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        InitializeComponent();
        WpfTheme.Attach(this);
        _controller = controller;

        Title = $"dev-term — Playback: {Path.GetFileName(controller.Path)}";
        DescriptionText.Text = controller.Description;

        _choices = [.. controller.AvailablePresenters.Select(name => new PresenterChoice(name, controller.Presenters.Contains(name, StringComparer.OrdinalIgnoreCase)))];
        PresenterChoices.ItemsSource = _choices;

        SpeedBox.ItemsSource = PlaybackController.Speeds;
        SpeedBox.SelectedItem = controller.Speed;

        foreach (var warning in controller.Log.Warnings)
        {
            OutputList.Items.Add(new PlaybackLine(warning, PlaybackLineKind.Status));
        }

        // Ticks only while the window is open; the controller does nothing while paused.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (_, _) => Pump(), Dispatcher);
        Closed += (_, _) => _timer.Stop();
        _timer.Start();

        Refresh();
    }

    internal PlaybackController Controller => _controller;

    /// <summary>One timer tick: plays whatever is due. Tests call it directly with an injected clock.</summary>
    internal void Pump()
    {
        if (_controller.Engine.IsPlaying)
        {
            Render(_controller.Tick());
        }
    }

    /// <summary>Runs an action and renders its batch — what every button does. Errors are shown, not thrown.</summary>
    internal void Do(Func<PlaybackBatch> action)
    {
        try
        {
            Render(action());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            OutputList.Items.Add(new PlaybackLine($"[error] {ex.Message}", PlaybackLineKind.Error));
            Refresh();
        }
    }

    /// <summary>Adds <see cref="NoteBox"/>'s text as a note at the current position and clears it.</summary>
    internal void AddNote()
    {
        var text = NoteBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        NoteBox.Text = string.Empty;
        Do(() => _controller.AddNote(text));
    }

    /// <summary>Saves the trim selection to <paramref name="path"/>, reporting the result in the output.</summary>
    internal void SaveSelection(string path)
    {
        Do(() =>
        {
            _controller.SaveSelection(path);
            OutputList.Items.Add(new PlaybackLine($"[dev-term] Saved records {_controller.SelectionStart}–{_controller.SelectionEnd} to {path}.", PlaybackLineKind.Status));
            return PlaybackBatch.Empty;
        });
    }

    private void Render(PlaybackBatch batch)
    {
        if (batch.Reset)
        {
            OutputList.Items.Clear();
        }

        // Only the last _maxOutputLines of a big batch (a seek to the end) are ever shown, so don't
        // add the rest just to remove them again.
        var lines = PlaybackText.Lines(batch).ToList();
        foreach (var line in lines.Skip(Math.Max(0, lines.Count - _maxOutputLines)))
        {
            OutputList.Items.Add(line);
        }

        while (OutputList.Items.Count > _maxOutputLines)
        {
            OutputList.Items.RemoveAt(0);
        }

        if (lines.Count > 0 && OutputList.Items.Count > 0)
        {
            OutputList.ScrollIntoView(OutputList.Items[^1]);
        }

        Refresh();
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            PositionText.Text = _controller.PositionText;
            PlayButton.Content = _controller.Engine.IsPlaying ? "❚❚ _Pause" : "▶ _Play";
            PositionSlider.Maximum = _controller.Engine.Count;
            PositionSlider.Value = _controller.Engine.Position;
            SpeedBox.SelectedItem = _controller.Speed;
            foreach (var choice in _choices)
            {
                choice.IsSelected = _controller.Presenters.Contains(choice.Name, StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void Rewind_Click(object sender, RoutedEventArgs e) => Do(_controller.Rewind);

    private void Step_Click(object sender, RoutedEventArgs e) => Do(_controller.Step);

    private void Play_Click(object sender, RoutedEventArgs e) => Do(_controller.TogglePlayPause);

    private void FastForward_Click(object sender, RoutedEventArgs e) => Do(_controller.FastForward);

    private void End_Click(object sender, RoutedEventArgs e) => Do(_controller.SkipToEnd);

    private void MarkIn_Click(object sender, RoutedEventArgs e) => Do(() =>
    {
        _controller.MarkIn();
        return PlaybackBatch.Empty;
    });

    private void MarkOut_Click(object sender, RoutedEventArgs e) => Do(() =>
    {
        _controller.MarkOut();
        return PlaybackBatch.Empty;
    });

    private void SaveSelection_Click(object sender, RoutedEventArgs e)
    {
        var suggested = _controller.DefaultTrimPath();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Selection as a New Log",
            FileName = Path.GetFileName(suggested),
            InitialDirectory = Path.GetDirectoryName(suggested),
            DefaultExt = SessionLogFormat.FileExtension,
            Filter = $"dev-term session logs (*{SessionLogFormat.FileExtension})|*{SessionLogFormat.FileExtension}|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            SaveSelection(dialog.FileName);
        }
    }

    private void AddNote_Click(object sender, RoutedEventArgs e) => AddNote();

    private void NoteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddNote();
        }
    }

    private void SpeedBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_updating && SpeedBox.SelectedItem is PlaybackSpeed speed)
        {
            _controller.SetSpeed(speed);
            Refresh();
        }
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updating)
        {
            Do(() => _controller.SeekTo((int)Math.Round(e.NewValue)));
        }
    }

    private void PresenterChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        // Read the box itself rather than relying on the two-way binding having already updated
        // the choice by the time Checked/Unchecked fires.
        if (sender is System.Windows.Controls.CheckBox { DataContext: PresenterChoice choice } box)
        {
            choice.IsSelected = box.IsChecked == true;
        }

        SetPresenterChoices();
    }

    /// <summary>Applies the check boxes' current state - what ticking/unticking one does.</summary>
    internal void SetPresenterChoices()
    {
        var chosen = _choices.Where(c => c.IsSelected).Select(c => c.Name).ToArray();
        if (chosen.Length == 0)
        {
            // At least one presenter always stays selected - put the check back.
            Refresh();
            return;
        }

        Do(() => _controller.SetPresenters(chosen));
    }

    /// <summary>One presenter check box.</summary>
    public sealed class PresenterChoice : INotifyPropertyChanged
    {
        private bool _isSelected;

        public PresenterChoice(string name, bool isSelected)
        {
            Name = name;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
