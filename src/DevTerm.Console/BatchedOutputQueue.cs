namespace DevTerm.Console;

/// <summary>
/// Coalesces a burst of output lines into a single pending UI update. <see cref="Enqueue"/> is called
/// from <c>Session</c>'s own background read-loop thread (once per rendered line) and only calls
/// <paramref name="scheduleDrain"/> (which marshals to the UI thread) the first time a line arrives
/// with no drain already pending - a fast stream that outruns the UI thread piles lines up here
/// instead of queuing one separate UI-thread closure per line. See
/// docs/bugs/resolved/031-tui-output-no-backpressure.md.
/// </summary>
internal sealed class BatchedOutputQueue(Action scheduleDrain)
{
    private readonly Lock _gate = new();
    private List<string> _pending = [];
    private bool _drainScheduled;

    public void Enqueue(string line)
    {
        bool schedule;
        lock (_gate)
        {
            _pending.Add(line);
            schedule = !_drainScheduled;
            _drainScheduled = true;
        }

        if (schedule)
        {
            scheduleDrain();
        }
    }

    /// <summary>
    /// Called from the UI thread (inside the scheduled drain): hands every line enqueued since the
    /// last drain to <paramref name="apply"/> in one call, then clears the pending flag so the next
    /// <see cref="Enqueue"/> schedules a fresh drain.
    /// </summary>
    public void Drain(Action<IReadOnlyList<string>> apply)
    {
        List<string> lines;
        lock (_gate)
        {
            lines = _pending;
            _pending = [];
            _drainScheduled = false;
        }

        if (lines.Count > 0)
        {
            apply(lines);
        }
    }
}
