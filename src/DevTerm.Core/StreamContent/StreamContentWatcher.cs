using System.Buffers;
using System.Diagnostics;
using DevTerm.Core.Presenters;

namespace DevTerm.Core.StreamContent;

/// <summary>Tuning for <see cref="StreamContentWatcher"/>.</summary>
public sealed class StreamContentWatcherOptions
{
    /// <summary>
    /// How long a capture with no in-band end (HP-GL, TIFF, unrecognized declared data) waits for
    /// more bytes before it's considered finished. Also how long a quiet link has to be before the
    /// next byte counts as the start of a new reply (where HP-GL is allowed to be recognized).
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>A capture is cut off (and reported as <see cref="StreamCaptureEnd.SizeLimit"/>) at this many bytes, so a runaway stream can't grow without bound.</summary>
    public int MaxCaptureBytes { get; set; } = 64 * 1024 * 1024;
}

/// <summary>
/// The Stream Monitor's detector (docs/design/proposals/stream-content-detection.md): an
/// <see cref="IPresenter"/> bound into a session's live <see cref="Pipeline"/> that watches the raw
/// incoming bytes for renderable/binary content, captures each detected stream in full, and raises
/// <see cref="ContentDetected"/> once it's complete. It never emits any rendered text itself
/// (<see cref="Render"/> always returns an empty list), so adding it leaves every other presenter's
/// output exactly as it was — the ordinary text/hex view of the same bytes carries on side by side.
/// </summary>
/// <remarks>
/// <para>
/// Detection is either declared (<see cref="ExpectResponse"/>: a sender that knows its command
/// returns, say, an image says so up front) or sniffed (<see cref="StreamContentSniffer"/>, for
/// everything else, including unsolicited data). A capture ends at the content's own structural
/// end (<see cref="StreamContentEndFinder"/>), a definite-length block's declared length, after
/// <see cref="StreamContentWatcherOptions.IdleTimeout"/> with no more bytes, at the size cap, or on
/// <see cref="Flush"/>.
/// </para>
/// <para>
/// Stateful per session, like every presenter here — one instance per <c>Session</c>, never
/// shared. <see cref="Render"/> is called on the session's read loop and never throws (a presenter
/// that throws faults the whole connection); <see cref="ContentDetected"/> is raised on that same
/// read-loop thread, or on a timer thread for an idle-ended capture, never while holding the
/// watcher's own lock.
/// </para>
/// </remarks>
public sealed class StreamContentWatcher : IPresenter, IStreamContentHintSink, IDisposable
{
    /// <summary>The name this watcher reports as a presenter.</summary>
    public const string PresenterName = "streamwatch";

    private readonly StreamContentWatcherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _idleTimer;
    private readonly Lock _gate = new();

    // Idle-state scanning: the unmatched tail of earlier reads, rescanned with the next one so a
    // signature split across two reads is still found.
    private byte[] _carry = [];
    private bool _carryStartsAtBoundary = true;
    private long _lastDataTimestamp;
    private bool _seenData;
    private StreamContentFormat? _pendingHint;

    // Capture state - _capture is non-null exactly while a capture is in progress.
    private MemoryStream? _capture;
    private StreamContentKind? _captureKind;
    private StreamContentFormat? _captureDeclaredFormat;
    private StreamContentEndFinder? _endFinder;
    private DateTimeOffset _captureStartedAt;
    private bool _disposed;

    // Captures completed under _gate, raised by whoever took the lock once it's released.
    private List<StreamCapture>? _completed;

    public StreamContentWatcher(StreamContentWatcherOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new StreamContentWatcherOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idleTimer = _timeProvider.CreateTimer(_ => OnIdleTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Raised once per completed capture. See the class remarks for which thread it's raised on.</summary>
    public event EventHandler<StreamCapture>? ContentDetected;

    public string Name => PresenterName;

    /// <summary>Whether a capture is currently in progress.</summary>
    public bool IsCapturing
    {
        get
        {
            lock (_gate)
            {
                return _capture is not null;
            }
        }
    }

    /// <inheritdoc />
    public void ExpectResponse(StreamContentFormat format)
    {
        if (format == StreamContentFormat.Text)
        {
            return;
        }

        lock (_gate)
        {
            _pendingHint = format;

            // Whatever arrives next is the start of the expected reply, not a continuation of the
            // stale tail of an earlier one.
            if (_capture is null)
            {
                _carry = [];
                _carryStartsAtBoundary = true;
            }
        }
    }

    /// <summary>Scans <paramref name="data"/>; always returns an empty list (see the class summary).</summary>
    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        List<StreamCapture>? completed = null;
        try
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    Process(data.IsSingleSegment ? data.FirstSpan : data.ToArray());
                }

                completed = TakeCompleted();
            }
        }
        catch (Exception ex)
        {
            // Never let a detection bug take the connection down with it - drop whatever was in
            // progress and carry on watching.
            Debug.WriteLine($"StreamContentWatcher: scan failed, discarding in-progress state: {ex}");
            lock (_gate)
            {
                ResetCapture();
                completed = TakeCompleted();
            }
        }

        Raise(completed);
        return [];
    }

    /// <summary>Ends a capture that's still in progress now (reported as <see cref="StreamCaptureEnd.Flushed"/>) instead of waiting for its end or the idle timeout.</summary>
    public void Flush()
    {
        List<StreamCapture>? completed = null;
        lock (_gate)
        {
            if (_capture is not null)
            {
                Complete(StreamCaptureEnd.Flushed);
            }

            completed = TakeCompleted();
        }

        Raise(completed);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ResetCapture();
        }

        _idleTimer.Dispose();
    }

    // Caller holds _gate.
    private void Process(ReadOnlySpan<byte> data)
    {
        var now = _timeProvider.GetTimestamp();
        if (_capture is null && _seenData && _timeProvider.GetElapsedTime(_lastDataTimestamp, now) >= _options.IdleTimeout)
        {
            // The link went quiet since the last read - this one starts a new reply.
            _carry = [];
            _carryStartsAtBoundary = true;
        }

        _lastDataTimestamp = now;
        _seenData = true;

        while (!data.IsEmpty)
        {
            data = _capture is not null ? AppendToCapture(data) : ScanForStart(data);
        }
    }

    // Caller holds _gate. Looks for the start of capturable content; returns the bytes from that
    // start onward (to be appended to the capture just begun), or empty if nothing started.
    private ReadOnlySpan<byte> ScanForStart(ReadOnlySpan<byte> data)
    {
        byte[] combined = [.. _carry, .. data];
        var startIsBoundary = _carryStartsAtBoundary;

        if (_pendingHint is { } hint)
        {
            // Skip a stray line terminator left over from the previous reply - only CR/LF, since
            // anything else may be the first byte of the binary reply itself.
            var start = 0;
            while (start < combined.Length && combined[start] is (byte)'\r' or (byte)'\n')
            {
                start++;
            }

            var reply = combined.AsSpan(start);
            switch (StreamContentSniffer.ParseBlockHeader(reply, out var headerLength, out var payloadLength))
            {
                case BlockHeaderStatus.Incomplete:
                    _carry = combined;
                    return [];

                case BlockHeaderStatus.Complete:
                    _pendingHint = null;
                    _carry = [];

                    // A zero-length block ("#10") has no content to capture.
                    if (payloadLength > 0)
                    {
                        BeginCapture(kind: null, hint, StreamContentEndFinder.FixedLength(payloadLength));
                    }

                    return reply[headerLength..];

                default:
                    if (reply.IsEmpty)
                    {
                        _carry = [];
                        return [];
                    }

                    _pendingHint = null;
                    _carry = [];
                    var declaredKind = StreamContentSniffer.Identify(reply);
                    BeginCapture(declaredKind, hint, declaredKind is null ? null : StreamContentEndFinder.For(declaredKind));
                    return reply;
            }
        }

        if (StreamContentSniffer.Find(combined, startIsBoundary) is { } match)
        {
            _carry = [];
            var endFinder = match.PayloadLength is { } length
                ? StreamContentEndFinder.FixedLength(length)
                : StreamContentEndFinder.For(match.Kind);
            BeginCapture(match.Kind, declaredFormat: null, endFinder);
            return combined.AsSpan(match.Offset + match.HeaderLength);
        }

        var keep = Math.Min(combined.Length, StreamContentSniffer.MaxSignatureLength);
        _carryStartsAtBoundary = keep == combined.Length
            ? startIsBoundary
            : combined[^(keep + 1)] is (byte)'\r' or (byte)'\n';
        _carry = combined[^keep..];
        return [];
    }

    // Caller holds _gate.
    private void BeginCapture(StreamContentKind? kind, StreamContentFormat? declaredFormat, StreamContentEndFinder? endFinder)
    {
        _capture = new MemoryStream();
        _captureKind = kind;
        _captureDeclaredFormat = declaredFormat;
        _endFinder = endFinder;
        _captureStartedAt = _timeProvider.GetUtcNow();
    }

    // Caller holds _gate. Returns whatever follows the end of the capture, if it ended within data.
    private ReadOnlySpan<byte> AppendToCapture(ReadOnlySpan<byte> data)
    {
        var capture = _capture!;
        var room = _options.MaxCaptureBytes - (int)capture.Length;
        var accepted = data.Length <= room ? data : data[..Math.Max(room, 0)];
        capture.Write(accepted);

        if (_endFinder?.FindEnd(capture.GetBuffer().AsSpan(0, (int)capture.Length)) is { } end)
        {
            var buffer = capture.GetBuffer();
            var overflow = buffer.AsSpan((int)end, (int)(capture.Length - end)).ToArray();
            capture.SetLength(end);
            Complete(StreamCaptureEnd.Complete);

            // Bytes past the end came from this read - they may begin something else.
            byte[] rest = [.. overflow, .. data[accepted.Length..]];
            return rest;
        }

        if (capture.Length >= _options.MaxCaptureBytes)
        {
            Complete(StreamCaptureEnd.SizeLimit);

            // Whatever didn't fit is the tail of that same runaway stream, not a new start.
            return [];
        }

        _idleTimer.Change(_options.IdleTimeout, Timeout.InfiniteTimeSpan);
        return [];
    }

    // Caller holds _gate.
    private void Complete(StreamCaptureEnd reason)
    {
        var data = _capture!.ToArray();
        var kind = _captureKind
            ?? StreamContentSniffer.Identify(data)
            ?? StreamContentKind.ForDeclaredFormat(_captureDeclaredFormat ?? StreamContentFormat.Binary);

        (_completed ??= []).Add(new StreamCapture(kind, data, _captureStartedAt, reason, _captureDeclaredFormat is not null));
        ResetCapture();
    }

    // Caller holds _gate.
    private void ResetCapture()
    {
        _capture?.Dispose();
        _capture = null;
        _captureKind = null;
        _captureDeclaredFormat = null;
        _endFinder = null;
        _carry = [];
        _carryStartsAtBoundary = true;
        if (!_disposed)
        {
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnIdleTimer()
    {
        List<StreamCapture>? completed = null;
        lock (_gate)
        {
            if (_capture is null || _disposed)
            {
                return;
            }

            var quietFor = _timeProvider.GetElapsedTime(_lastDataTimestamp);
            if (quietFor < _options.IdleTimeout)
            {
                // More data arrived after this timer was armed - wait out the rest of the gap.
                _idleTimer.Change(_options.IdleTimeout - quietFor, Timeout.InfiniteTimeSpan);
                return;
            }

            Complete(StreamCaptureEnd.IdleTimeout);
            completed = TakeCompleted();
        }

        Raise(completed);
    }

    // Caller holds _gate.
    private List<StreamCapture>? TakeCompleted()
    {
        var completed = _completed;
        _completed = null;
        return completed;
    }

    private void Raise(List<StreamCapture>? completed)
    {
        if (completed is null)
        {
            return;
        }

        foreach (var capture in completed)
        {
            try
            {
                ContentDetected?.Invoke(this, capture);
            }
            catch (Exception ex)
            {
                // A subscriber's failure (a full disk while saving, say) must not escape into the
                // session's read loop and fault the connection.
                Debug.WriteLine($"StreamContentWatcher.ContentDetected handler threw: {ex}");
            }
        }
    }
}
