using System.Buffers;
using DevTerm.Core.Presenters;

namespace DevTerm.Logging.Playback;

/// <summary>One record played back, with whatever the playback presenters rendered from it (only an <c>rx</c> record is ever rendered).</summary>
public sealed record PlaybackItem(int Index, SessionLogRecord Record, TimeSpan Offset, IReadOnlyList<PresenterOutput> Outputs);

/// <summary>
/// What one engine call produced: the items played, in order, and whether the display should be
/// cleared first (<see cref="Reset"/>) because playback went back to the start — a rewind, a
/// backward seek, or a presenter change, all of which replay from record 0 so stateful presenters
/// (the ASCII line buffer, a protocol decoder mid-frame) see exactly the byte stream they saw live.
/// </summary>
public sealed record PlaybackBatch(bool Reset, IReadOnlyList<PlaybackItem> Items)
{
    public static PlaybackBatch Empty { get; } = new(false, []);
}

/// <summary>
/// Replays a <see cref="SessionLog"/> through a presenter <see cref="Pipeline"/>, honoring the
/// recorded timing scaled by <see cref="Speed"/>. Pull-driven and single-threaded: a front end calls
/// <see cref="Tick"/> from its UI timer and renders the returned batch, so nothing here raises events
/// on a background thread, and a test drives it deterministically with a fake
/// <see cref="TimeProvider"/>. Never touches a transport — the only thing it feeds is the pipeline.
/// See docs/design/session-logging.md.
/// </summary>
public sealed class PlaybackEngine
{
    /// <summary>How many records one <see cref="Tick"/> plays at most, so "as fast as possible" over a huge log still hands control back to the UI between batches.</summary>
    public const int DefaultMaxRecordsPerTick = 2000;

    private readonly TimeProvider _clock;
    private Func<Pipeline> _pipelineFactory;
    private Pipeline _pipeline;
    private double _speed = 1.0;

    // Playback timing is anchored: at _anchorTimestamp (clock) the log was at _anchorOffset, and
    // log time advances at Speed x clock time from there. Re-anchored on every play, seek, step and
    // speed change, so pausing and changing speed never jump.
    private long _anchorTimestamp;
    private TimeSpan _anchorOffset;

    public PlaybackEngine(SessionLog log, Func<Pipeline> pipelineFactory, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(pipelineFactory);
        Log = log;
        _pipelineFactory = pipelineFactory;
        _pipeline = pipelineFactory();
        _clock = clock ?? TimeProvider.System;
    }

    public SessionLog Log { get; }

    /// <summary>How many records have been played — also the index of the next one to play (0 to <see cref="Count"/>).</summary>
    public int Position { get; private set; }

    public int Count => Log.Records.Count;

    public bool IsPlaying { get; private set; }

    public bool IsAtEnd => Position >= Count;

    /// <summary>The log time of the last record played (zero before the first).</summary>
    public TimeSpan Elapsed => Position == 0 ? TimeSpan.Zero : Log.OffsetOf(Position - 1);

    public TimeSpan Duration => Log.Duration;

    /// <summary>Playback rate relative to real time: 1 is realtime, 0.25 is quarter speed, <see cref="double.PositiveInfinity"/> is as fast as possible (no waiting at all).</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            if (double.IsNaN(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Speed must be positive (PositiveInfinity for as fast as possible).");
            }

            // Keep the log time reached so far at the old speed; only what follows runs at the new one.
            Reanchor(CurrentLogTime());
            _speed = value;
        }
    }

    public void Play()
    {
        if (IsAtEnd || IsPlaying)
        {
            return;
        }

        IsPlaying = true;
        Reanchor(_anchorOffset);
    }

    /// <summary>Pauses where it is — part-way through a gap between two records, resuming waits only for what's left of it.</summary>
    public void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        Reanchor(CurrentLogTime());
        IsPlaying = false;
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Plays every record that's due by now at the current speed (up to <paramref name="maxRecords"/>); pauses itself at the end. A no-op while paused.</summary>
    public PlaybackBatch Tick(int maxRecords = DefaultMaxRecordsPerTick)
    {
        if (!IsPlaying)
        {
            return PlaybackBatch.Empty;
        }

        var infinite = double.IsPositiveInfinity(_speed);
        var due = infinite ? TimeSpan.MaxValue : CurrentLogTime();

        var items = new List<PlaybackItem>();
        while (!IsAtEnd && items.Count < maxRecords && Log.OffsetOf(Position) <= due)
        {
            items.Add(PlayNext());
        }

        if (infinite)
        {
            Reanchor(Elapsed);
        }

        if (IsAtEnd)
        {
            Reanchor(Elapsed);
            IsPlaying = false;
        }

        return new PlaybackBatch(false, items);
    }

    /// <summary>How long until the next record is due at the current speed — what a front end's timer (or the CLI's delay) waits for. <see langword="null"/> while paused or at the end.</summary>
    public TimeSpan? TimeUntilNextDue()
    {
        if (!IsPlaying || IsAtEnd)
        {
            return null;
        }

        if (double.IsPositiveInfinity(_speed))
        {
            return TimeSpan.Zero;
        }

        var logTimeAhead = Log.OffsetOf(Position) - CurrentLogTime();
        return logTimeAhead <= TimeSpan.Zero ? TimeSpan.Zero : logTimeAhead / _speed;
    }

    /// <summary>Pauses and plays exactly one record. Empty at the end.</summary>
    public PlaybackBatch Step()
    {
        Pause();
        if (IsAtEnd)
        {
            return PlaybackBatch.Empty;
        }

        var item = PlayNext();
        Reanchor();
        return new PlaybackBatch(false, [item]);
    }

    /// <summary>Back to the start with fresh presenters (so buffered partial lines/frames from later in the log don't leak in). Keeps playing if it was.</summary>
    public PlaybackBatch Rewind()
    {
        _pipeline = _pipelineFactory();
        Position = 0;
        Reanchor();
        return new PlaybackBatch(true, []);
    }

    /// <summary>
    /// Moves to <paramref name="position"/> (clamped to 0..<see cref="Count"/>). Forward plays every
    /// record in between instantly; backward rewinds and replays from the start — either way the
    /// presenters end up in exactly the state live capture left them in at that point.
    /// </summary>
    public PlaybackBatch SeekTo(int position)
    {
        position = Math.Clamp(position, 0, Count);
        var reset = false;
        if (position < Position)
        {
            Rewind();
            reset = true;
        }

        var items = new List<PlaybackItem>();
        while (Position < position)
        {
            items.Add(PlayNext());
        }

        if (IsAtEnd)
        {
            IsPlaying = false;
        }

        Reanchor();
        return new PlaybackBatch(reset, items);
    }

    /// <summary>Skips ahead <paramref name="amount"/> of log time, playing every record in that span instantly.</summary>
    public PlaybackBatch FastForward(TimeSpan amount)
    {
        var target = Elapsed + amount;
        var position = Position;
        while (position < Count && Log.OffsetOf(position) <= target)
        {
            position++;
        }

        // Always make progress, even across a gap longer than amount.
        return SeekTo(Math.Max(position, Math.Min(Position + 1, Count)));
    }

    public PlaybackBatch SkipToEnd() => SeekTo(Count);

    /// <summary>Switches to a different set of presenters and replays up to the current position through them.</summary>
    public PlaybackBatch ChangePipeline(Func<Pipeline> pipelineFactory)
    {
        ArgumentNullException.ThrowIfNull(pipelineFactory);
        _pipelineFactory = pipelineFactory;
        var position = Position;
        Rewind();
        var batch = SeekTo(position);
        return batch with { Reset = true };
    }

    /// <summary>Adds a note at the current position (right after the last record played) and plays it, so it shows immediately. The caller saves the log.</summary>
    public PlaybackBatch AddNote(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Log.InsertNote(Position, text);
        var item = PlayNext();
        Reanchor();
        return new PlaybackBatch(false, [item]);
    }

    private PlaybackItem PlayNext()
    {
        var index = Position;
        var record = Log.Records[index];
        IReadOnlyList<PresenterOutput> outputs = [];
        if (record.Kind == SessionLogRecordKind.Rx && !record.Data.IsEmpty)
        {
            try
            {
                outputs = _pipeline.Render(new ReadOnlySequence<byte>(record.Data));
            }
            catch (Exception ex)
            {
                outputs = [new PresenterOutput("error", $"A presenter failed on record {index + 1}: {ex.Message}")];
            }
        }

        Position = index + 1;
        return new PlaybackItem(index, record, Log.OffsetOf(index), outputs);
    }

    // Where playback is in log time right now: frozen while paused (or at Max speed, where Tick
    // keeps it at the last record played), advancing at Speed x real time while playing.
    private TimeSpan CurrentLogTime() =>
        IsPlaying && !double.IsPositiveInfinity(_speed)
            ? _anchorOffset + (_clock.GetElapsedTime(_anchorTimestamp) * _speed)
            : _anchorOffset;

    private void Reanchor() => Reanchor(Elapsed);

    private void Reanchor(TimeSpan logTime)
    {
        _anchorTimestamp = _clock.GetTimestamp();
        _anchorOffset = logTime;
    }
}
