using DevTerm.Configuration;
using DevTerm.Logging;
using DevTerm.Logging.Playback;
using Terminal.Gui.App;
using Terminal.Gui.Editor;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI's Playback window (File &gt; Open Log for Playback...): replays a session log through a
/// chosen set of presenters with transport controls, trim and notes. All behavior lives in the shared
/// <see cref="PlaybackController"/>; this only draws it. Never touches a transport. See
/// docs/specs/playback-window.md.
/// </summary>
public static class PlaybackMode
{
    /// <summary>How often the playback timer ticks while the window is open.</summary>
    internal static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(40);

    private const int _maxOutputLines = 300;

    /// <summary>
    /// Asks for a log path (defaulting to the newest log in <see cref="DevTermUserDataPaths.LogsDirectory"/>),
    /// opens it, and runs the Playback window as a nested modal. Errors are shown, not thrown.
    /// </summary>
    public static void OpenAndRun(IApplication app, CliOptions cliOptions)
    {
        var path = PromptForText(app, "Open Log for Playback", "Log file:", NewestLog() ?? DevTermUserDataPaths.LogsDirectory + Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        PlaybackController controller;
        try
        {
            controller = new PlaybackPresenters(cliOptions).Open(path.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SessionLogFormatException or ArgumentException or NotSupportedException)
        {
            MessageBox.ErrorQuery(app, "dev-term — playback", $"Could not open '{path}' for playback: {ex.Message}", "Ok");
            return;
        }

        var parts = BuildWindow(app, controller);
        var timer = app.AddTimeout(TickInterval, () =>
        {
            parts.Pump();
            return true;
        });
        try
        {
            app.Run(parts.Window);
        }
        finally
        {
            if (timer is not null)
            {
                app.RemoveTimeout(timer);
            }
        }
    }

    /// <summary>The newest <c>.jsonl</c> file in the logs directory, or <see langword="null"/> if there isn't one.</summary>
    internal static string? NewestLog()
    {
        try
        {
            var directory = new DirectoryInfo(DevTermUserDataPaths.LogsDirectory);
            return directory.Exists
                ? directory.GetFiles("*" + SessionLogFormat.FileExtension).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the window over <paramref name="controller"/> without running it — the seam tests
    /// drive headlessly. <see cref="PlaybackWindowParts.Pump"/> is what the real timer calls; tests
    /// call it directly (a headless test has no running loop for the timer).
    /// </summary>
    internal static PlaybackWindowParts BuildWindow(IApplication app, PlaybackController controller)
    {
        var window = new Window
        {
            Title = $"dev-term — Playback: {Path.GetFileName(controller.Path)}",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        var description = new Label { X = 0, Y = 0, Width = Dim.Fill(), Text = controller.Description };

        var presentersLabel = new Label { X = 0, Y = 1, Text = "Presenters:" };
        var presentersField = new TextField
        {
            X = Pos.Right(presentersLabel) + 1,
            Y = 1,
            Width = 28,
            Text = string.Join(", ", controller.Presenters),
        };
        var availableLabel = new Label
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Text = $"  (Enter applies) {string.Join(" ", controller.AvailablePresenters)}",
        };

        var outputLines = new List<string>();
        var output = new Editor
        {
            X = 0,
            Y = 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(3),
            ReadOnly = true,

            // Soft-wrapped like the main window's output pane: long lines stay readable from their start.
            WordWrap = true,
        };

        var positionLabel = new Label { X = 0, Y = Pos.Bottom(output), Width = Dim.Fill() };

        var transportRow = Pos.Bottom(positionLabel);
        var rewindButton = new Button { X = 0, Y = transportRow, Text = "_Rewind", ShadowStyle = ShadowStyles.None };
        var stepButton = new Button { X = Pos.Right(rewindButton) + 1, Y = transportRow, Text = "S_tep", ShadowStyle = ShadowStyles.None };
        var playButton = new Button { X = Pos.Right(stepButton) + 1, Y = transportRow, Text = "_Play", ShadowStyle = ShadowStyles.None };
        var forwardButton = new Button { X = Pos.Right(playButton) + 1, Y = transportRow, Text = "+10_s", ShadowStyle = ShadowStyles.None };
        var endButton = new Button { X = Pos.Right(forwardButton) + 1, Y = transportRow, Text = "_End", ShadowStyle = ShadowStyles.None };
        var slowerButton = new Button { X = Pos.Right(endButton) + 1, Y = transportRow, Text = "Slo_wer", ShadowStyle = ShadowStyles.None };
        var fasterButton = new Button { X = Pos.Right(slowerButton) + 1, Y = transportRow, Text = "_Faster", ShadowStyle = ShadowStyles.None };

        var editRow = Pos.Bottom(positionLabel) + 1;
        var markInButton = new Button { X = 0, Y = editRow, Text = "Mark _In", ShadowStyle = ShadowStyles.None };
        var markOutButton = new Button { X = Pos.Right(markInButton) + 1, Y = editRow, Text = "Mark _Out", ShadowStyle = ShadowStyles.None };
        var saveButton = new Button { X = Pos.Right(markOutButton) + 1, Y = editRow, Text = "Sa_ve Selection...", ShadowStyle = ShadowStyles.None };
        var noteButton = new Button { X = Pos.Right(saveButton) + 1, Y = editRow, Text = "Add _Note...", ShadowStyle = ShadowStyles.None };
        var closeButton = new Button { X = Pos.Right(noteButton) + 1, Y = editRow, Text = "_Close", ShadowStyle = ShadowStyles.None };

        void Append(IEnumerable<string> lines)
        {
            outputLines.AddRange(lines);
            if (outputLines.Count > _maxOutputLines)
            {
                outputLines.RemoveRange(0, outputLines.Count - _maxOutputLines);
            }

            output.Text = string.Join('\n', outputLines);
            output.CaretOffset = output.Text.Length;
        }

        void Refresh()
        {
            positionLabel.Text = controller.PositionText;
            playButton.Text = controller.Engine.IsPlaying ? "_Pause" : "_Play";
            presentersField.Text = string.Join(", ", controller.Presenters);
        }

        void Render(PlaybackBatch batch)
        {
            if (batch.Reset)
            {
                outputLines.Clear();
                output.Text = string.Empty;
            }

            if (batch.Items.Count > 0)
            {
                // The TUI output pane is plain text, so line kinds are already told apart by their
                // [tx]/[note]/[error]/[dev-term] tags.
                Append(PlaybackText.Lines(batch).Select(l => l.Text));
            }

            Refresh();
        }

        void Do(Func<PlaybackBatch> action)
        {
            try
            {
                Render(action());
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery(app, "dev-term — playback", ex.Message, "Ok");
                Refresh();
            }
        }

        void OnAccept(Button button, Func<PlaybackBatch> action) =>
            button.Accepting += (_, e) =>
            {
                Do(action);
                e.Handled = true;
            };

        void ChangeSpeed(int by)
        {
            var speeds = PlaybackController.Speeds;
            var index = Math.Clamp(speeds.ToList().IndexOf(controller.Speed) + by, 0, speeds.Count - 1);
            controller.SetSpeed(speeds[index]);
        }

        OnAccept(rewindButton, controller.Rewind);
        OnAccept(stepButton, controller.Step);
        OnAccept(playButton, controller.TogglePlayPause);
        OnAccept(forwardButton, controller.FastForward);
        OnAccept(endButton, controller.SkipToEnd);
        OnAccept(slowerButton, () =>
        {
            ChangeSpeed(-1);
            return PlaybackBatch.Empty;
        });
        OnAccept(fasterButton, () =>
        {
            ChangeSpeed(+1);
            return PlaybackBatch.Empty;
        });
        OnAccept(markInButton, () =>
        {
            controller.MarkIn();
            return PlaybackBatch.Empty;
        });
        OnAccept(markOutButton, () =>
        {
            controller.MarkOut();
            return PlaybackBatch.Empty;
        });
        OnAccept(saveButton, () =>
        {
            if (PromptForText(app, "Save Selection", "Save as:", controller.DefaultTrimPath()) is { Length: > 0 } target)
            {
                controller.SaveSelection(target.Trim());
                Append([$"[dev-term] Saved records {controller.SelectionStart}–{controller.SelectionEnd} to {target.Trim()}."]);
            }

            return PlaybackBatch.Empty;
        });
        OnAccept(noteButton, () =>
            PromptForText(app, "Add Note", "Note:", string.Empty) is { } text && text.Trim().Length > 0
                ? controller.AddNote(text)
                : PlaybackBatch.Empty);
        closeButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        presentersField.Accepting += (_, e) =>
        {
            Do(() => controller.SetPresenters(presentersField.Text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)));
            e.Handled = true;
        };

        window.Add(
            description, presentersLabel, presentersField, availableLabel, output, positionLabel,
            rewindButton, stepButton, playButton, forwardButton, endButton, slowerButton, fasterButton,
            markInButton, markOutButton, saveButton, noteButton, closeButton);

        foreach (var warning in controller.Log.Warnings)
        {
            Append([$"[dev-term] {warning}"]);
        }

        Refresh();

        void Pump()
        {
            if (controller.Engine.IsPlaying)
            {
                Render(controller.Tick());
            }
        }

        return new PlaybackWindowParts(window, controller, output, positionLabel, presentersField, playButton, Pump, Do);
    }

    /// <summary>A small modal asking for one line of text; <see langword="null"/> if cancelled.</summary>
    internal static string? PromptForText(IApplication app, string title, string label, string initial)
    {
        string? result = null;
        var dialog = new Dialog { Title = title, Width = 76, Height = 8 };
        var prompt = new Label { X = 0, Y = 0, Text = label };
        var field = new TextField { X = 0, Y = 1, Width = Dim.Fill(), Text = initial };
        var okButton = new Button { X = 0, Y = 3, Text = "OK", IsDefault = true };
        var cancelButton = new Button { X = Pos.Right(okButton) + 1, Y = 3, Text = "Cancel" };

        void Accept()
        {
            result = field.Text;
            app.RequestStop();
        }

        field.Accepting += (_, e) =>
        {
            e.Handled = true;
            Accept();
        };
        okButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Accept();
        };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };
        dialog.Add(prompt, field, okButton, cancelButton);
        field.SetFocus();
        field.MoveEnd();
        app.Run(dialog);
        return result;
    }
}

/// <summary>The Playback window's controls, for tests: <see cref="Pump"/> is one timer tick; <see cref="Do"/> runs an action and renders its batch, exactly as a button does.</summary>
internal sealed record PlaybackWindowParts(Window Window, PlaybackController Controller, Editor Output, Label PositionLabel, TextField PresentersField, Button PlayButton, Action Pump, Action<Func<PlaybackBatch>> Do);
