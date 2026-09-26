namespace DevTerm.Test.Utilities;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test calls <see cref="Advance"/> — for
/// deterministic tests of anything that measures elapsed time (the session logger's timestamps,
/// the playback scheduler). Only the wall clock and the timestamp are faked; timers aren't used by
/// either, so <see cref="TimeProvider.CreateTimer"/> is left as the system one.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public ManualTimeProvider(DateTimeOffset? start = null) =>
        _utcNow = start ?? new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan by)
    {
        _utcNow += by;
        _timestamp += by.Ticks;
    }
}
