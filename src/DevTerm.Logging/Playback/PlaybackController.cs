using System.Globalization;
using DevTerm.Core.Presenters;

namespace DevTerm.Logging.Playback;

/// <summary>A playback speed choice: its label and rate (<see cref="double.PositiveInfinity"/> for Max).</summary>
public sealed record PlaybackSpeed(string Label, double Rate)
{
    public override string ToString() => Label;
}

/// <summary>
/// Everything a Playback window does, independent of how it's drawn — shared by the TUI's
/// <c>PlaybackMode</c> and WPF's <c>PlaybackWindow</c> so the two can't drift: the transport
/// controls (over <see cref="PlaybackEngine"/>), which presenters to replay through, the trim
/// selection, and adding notes. Every action returns the <see cref="PlaybackBatch"/> to render.
/// See docs/specs/playback-window.md.
/// </summary>
public sealed class PlaybackController
{
    /// <summary>The speed choices, slowest first: 0.25x, 0.5x, 1x (realtime), 2x, 10x, Max.</summary>
    public static IReadOnlyList<PlaybackSpeed> Speeds { get; } =
    [
        new("0.25x", 0.25),
        new("0.5x", 0.5),
        new("1x", 1),
        new("2x", 2),
        new("10x", 10),
        new("Max", double.PositiveInfinity),
    ];

    /// <summary>How far Fast-forward skips, in log time.</summary>
    public static readonly TimeSpan FastForwardAmount = TimeSpan.FromSeconds(10);

    private readonly Func<IReadOnlyList<string>, Pipeline> _pipelineFor;

    /// <param name="pipelineFor">Builds a pipeline of fresh presenter instances for the given names — called again on every rewind, so no presenter state carries over.</param>
    /// <param name="availablePresenters">Every presenter name that can be chosen (the same catalog the main window uses).</param>
    public PlaybackController(string path, SessionLog log, Func<IReadOnlyList<string>, Pipeline> pipelineFor, IReadOnlyList<string> availablePresenters, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(pipelineFor);
        ArgumentNullException.ThrowIfNull(availablePresenters);

        Path = path;
        _pipelineFor = pipelineFor;
        AvailablePresenters = availablePresenters;

        // Start with what was displayed while capturing, limited to what's installed now.
        var initial = log.Header.Presenters.Where(p => availablePresenters.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
        Presenters = initial.Length > 0 ? initial : [DefaultPresenter(availablePresenters)];

        var presenters = Presenters;
        Engine = new PlaybackEngine(log, () => _pipelineFor(presenters), clock);
        SelectionEnd = log.Records.Count;
    }

    /// <summary>Loads <paramref name="path"/> (see <see cref="SessionLog.Load"/>).</summary>
    public static PlaybackController Open(string path, Func<IReadOnlyList<string>, Pipeline> pipelineFor, IReadOnlyList<string> availablePresenters, TimeProvider? clock = null) =>
        new(path, SessionLog.Load(path), pipelineFor, availablePresenters, clock);

    public string Path { get; }

    public SessionLog Log => Engine.Log;

    public PlaybackEngine Engine { get; }

    public IReadOnlyList<string> AvailablePresenters { get; }

    public IReadOnlyList<string> Presenters { get; private set; }

    public PlaybackSpeed Speed { get; private set; } = Speeds[2];

    /// <summary>The trim selection as positions: records <see cref="SelectionStart"/> (inclusive) to <see cref="SelectionEnd"/> (exclusive). Starts as the whole log.</summary>
    public int SelectionStart { get; private set; }

    /// <inheritdoc cref="SelectionStart"/>
    public int SelectionEnd { get; private set; }

    public bool HasSelection => SelectionEnd > SelectionStart;

    /// <summary>A one-line summary of the log, for the window's header line (the file name is in the title): <c>tek2230 (tcp://192.168.0.107:23) · 2026-09-25 12:00:00 · 8 records</c>.</summary>
    public string Description
    {
        get
        {
            var header = Log.Header;
            var subject = header.Profile is { Length: > 0 } profile ? $"{profile} ({header.Connection})" : header.Connection ?? "unknown connection";
            var trimmed = header.TrimmedFrom is { } from ? $" · trimmed from {from}" : string.Empty;
            return $"{subject} · {header.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} · {Log.Records.Count} records{trimmed}";
        }
    }

    /// <summary>The position indicator: <c>▶ 12/340  00:05.123 / 01:12.000  1x  [in 0 – out 340]</c>.</summary>
    public string PositionText
    {
        get
        {
            var state = Engine.IsPlaying ? "Playing" : Engine.IsAtEnd && Engine.Count > 0 ? "End" : "Paused";
            return $"{state}  {Engine.Position}/{Engine.Count}  {PlaybackText.FormatOffset(Engine.Elapsed)} / {PlaybackText.FormatOffset(Engine.Duration)}  {Speed.Label}  Selection {SelectionStart}–{SelectionEnd}";
        }
    }

    /// <summary>Play if paused (from the start again if at the end), pause if playing.</summary>
    public PlaybackBatch TogglePlayPause()
    {
        if (Engine.IsPlaying)
        {
            Engine.Pause();
            return PlaybackBatch.Empty;
        }

        var batch = PlaybackBatch.Empty;
        if (Engine.IsAtEnd)
        {
            batch = Engine.Rewind();
        }

        Engine.Play();
        return batch;
    }

    public PlaybackBatch Tick() => Engine.Tick();

    public PlaybackBatch Step() => Engine.Step();

    public PlaybackBatch Rewind() => Engine.Rewind();

    public PlaybackBatch FastForward() => Engine.FastForward(FastForwardAmount);

    public PlaybackBatch SkipToEnd() => Engine.SkipToEnd();

    public PlaybackBatch SeekTo(int position) => Engine.SeekTo(position);

    public void SetSpeed(PlaybackSpeed speed)
    {
        ArgumentNullException.ThrowIfNull(speed);
        Engine.Speed = speed.Rate;
        Speed = speed;
    }

    /// <summary>Replays up to the current position through <paramref name="names"/> instead (unknown names are ignored; an empty choice keeps the current presenters).</summary>
    public PlaybackBatch SetPresenters(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var chosen = names
            .Select(n => n.Trim())
            .Select(n => AvailablePresenters.FirstOrDefault(a => string.Equals(a, n, StringComparison.OrdinalIgnoreCase)))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (chosen.Length == 0)
        {
            return PlaybackBatch.Empty;
        }

        Presenters = chosen;
        return Engine.ChangePipeline(() => _pipelineFor(chosen));
    }

    /// <summary>Trim starts at the current position (the next record to play).</summary>
    public void MarkIn()
    {
        SelectionStart = Engine.Position;
        if (SelectionEnd < SelectionStart)
        {
            SelectionEnd = Engine.Count;
        }
    }

    /// <summary>Trim ends at the current position (after the last record played).</summary>
    public void MarkOut()
    {
        SelectionEnd = Engine.Position;
        if (SelectionStart > SelectionEnd)
        {
            SelectionStart = 0;
        }
    }

    /// <summary>Writes the selected records to <paramref name="path"/> as a new log.</summary>
    /// <exception cref="InvalidOperationException">The selection is empty.</exception>
    public void SaveSelection(string path)
    {
        if (!HasSelection)
        {
            throw new InvalidOperationException("Nothing is selected: Mark In must come before Mark Out.");
        }

        if (string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(Path), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Save the trimmed log under a new name - it would replace the log being played.");
        }

        Log.Trim(SelectionStart, SelectionEnd, System.IO.Path.GetFileName(Path)).Save(path);
    }

    /// <summary>The default file name for a trimmed copy: <c>{name}.trim-{start}-{end}.jsonl</c> next to the original.</summary>
    public string DefaultTrimPath() =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? string.Empty,
            $"{System.IO.Path.GetFileNameWithoutExtension(Path)}.trim-{SelectionStart}-{SelectionEnd}{SessionLogFormat.FileExtension}");

    /// <summary>Adds a note at the current position and saves the log in place, so the note is part of the file straight away.</summary>
    public PlaybackBatch AddNote(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var index = Engine.Position;
        var batch = Engine.AddNote(text.Trim());

        // The note is a new record at index: selection bounds past it move with the records they mark.
        if (SelectionEnd >= index)
        {
            SelectionEnd++;
        }

        if (SelectionStart > index)
        {
            SelectionStart++;
        }

        Log.Save(Path);
        return batch;
    }

    private static string DefaultPresenter(IReadOnlyList<string> available) =>
        available.FirstOrDefault(p => string.Equals(p, "hex", StringComparison.OrdinalIgnoreCase))
        ?? available.FirstOrDefault()
        ?? throw new ArgumentException("No presenters are available to play back through.", nameof(available));
}
