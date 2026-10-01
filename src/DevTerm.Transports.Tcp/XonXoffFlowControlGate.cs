namespace DevTerm.Transports.Tcp;

/// <summary>
/// Shared pause/resume state for software (XON/XOFF) flow control: written by
/// <see cref="XonXoffReadStream"/> as it scans inbound bytes for XON (0x11)/XOFF (0x13), read by
/// <see cref="TcpTransport.WriteAsync"/> before it writes. A peer-sent XOFF pauses further writes
/// until the next XON - the software equivalent of the hardware RTS/CTS handshake serial already
/// honors, needed here because a serial-to-Ethernet bridge has no separate control line to carry it
/// over TCP and instead forwards the attached device's XON/XOFF bytes in-band.
///
/// <see cref="Enabled"/> is toggleable at runtime (<see cref="TcpTransport.SoftwareFlowControl"/>) -
/// a bridge's actual behavior often can't be confirmed until a connection is already open, so the
/// feature doesn't have to be decided before connecting or require a reconnect to change. Disabling
/// it releases any write currently paused on a stale XOFF rather than leaving it stuck forever.
/// </summary>
internal sealed class XonXoffFlowControlGate
{
    private readonly object _lock = new();
    private TaskCompletionSource? _pausedUntilXon;
    private bool _enabled;

    public bool Enabled
    {
        get
        {
            lock (_lock)
            {
                return _enabled;
            }
        }

        set
        {
            TaskCompletionSource? toRelease = null;
            lock (_lock)
            {
                _enabled = value;
                if (!value)
                {
                    toRelease = _pausedUntilXon;
                    _pausedUntilXon = null;
                }
            }

            toRelease?.TrySetResult();
        }
    }

    public void OnXoffReceived()
    {
        lock (_lock)
        {
            if (!_enabled)
            {
                return;
            }

            _pausedUntilXon ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void OnXonReceived()
    {
        TaskCompletionSource? toRelease;
        lock (_lock)
        {
            toRelease = _pausedUntilXon;
            _pausedUntilXon = null;
        }

        toRelease?.TrySetResult();
    }

    public async Task WaitUntilResumedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? pause;
            lock (_lock)
            {
                pause = _enabled ? _pausedUntilXon?.Task : null;
            }

            if (pause is null)
            {
                return;
            }

            await pause.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
