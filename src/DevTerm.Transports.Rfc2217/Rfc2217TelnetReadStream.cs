namespace DevTerm.Transports.Rfc2217;

/// <summary>
/// Wraps a raw TCP <see cref="Stream"/> to separate RFC 2217's two logically distinct channels
/// that share one socket: Telnet-escaped application data (read/write) and Telnet option
/// negotiation / COM-PORT-OPTION subnegotiations (handled here, never handed to the caller).
/// Does not own <paramref name="inner"/> — the caller (<see cref="Rfc2217Transport"/>) disposes
/// the underlying connection itself.
/// </summary>
/// <remarks>
/// A resumable byte-at-a-time state machine, because a Telnet control sequence can split across
/// two separate socket reads. A read that only consumed control bytes must never return 0 to its
/// caller — <see cref="DevTerm.Core.Transports.StreamToPipePump"/> treats a 0-byte read as EOF —
/// so <see cref="ReadAsync"/> keeps pulling from <paramref name="inner"/> until it has real data,
/// genuine inner-stream EOF, or cancellation.
/// </remarks>
internal sealed class Rfc2217TelnetReadStream(Stream inner) : Stream
{
    private enum ReadState
    {
        Data,
        SawIac,
        SawCommand,
        SawSbWaitingOption,
        InSubnegotiation,
        InSubnegotiationSawIac,
    }

    private readonly Stream _inner = inner;
    private readonly byte[] _readScratch = new byte[4096];
    private readonly List<byte> _pendingData = [];
    private readonly List<byte> _subnegotiationBuffer = [];
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private ReadState _state = ReadState.Data;
    private byte _pendingTelnetCommand;
    private byte _subnegotiationOption;
    private bool _disposed;

    /// <summary>Raised when the peer answers our COM-PORT-OPTION offer: <c>true</c> for WILL/DO, <c>false</c> for WONT/DONT.</summary>
    public event Action<bool>? ComPortOptionNegotiationReceived;

    /// <summary>Raised for every decoded COM-PORT-OPTION subnegotiation (acks and NOTIFY-* pushes alike).</summary>
    public event Action<IRfc2217Message>? ComPortMessageReceived;

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Sends bytes that are already fully framed (a Telnet option offer, an encoded COM-PORT-OPTION subnegotiation) with no further escaping.</summary>
    public ValueTask SendRawFramedAsync(ReadOnlyMemory<byte> alreadyFramedBytes, CancellationToken cancellationToken = default) =>
        SendRawLockedAsync(alreadyFramedBytes, cancellationToken);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            if (_pendingData.Count > 0)
            {
                var n = Math.Min(buffer.Length, _pendingData.Count);
                for (var i = 0; i < n; i++)
                {
                    buffer.Span[i] = _pendingData[i];
                }

                _pendingData.RemoveRange(0, n);
                return n;
            }

            var bytesRead = await _inner.ReadAsync(_readScratch, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return 0;
            }

            await ProcessChunkAsync(_readScratch.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        SendRawLockedAsync(Rfc2217Codec.Escape(buffer.Span), cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writeLock.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    private async ValueTask ProcessChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken)
    {
        for (var i = 0; i < chunk.Length; i++)
        {
            var b = chunk.Span[i];
            switch (_state)
            {
                case ReadState.Data:
                    if (b == Telnet.Iac)
                    {
                        _state = ReadState.SawIac;
                    }
                    else
                    {
                        _pendingData.Add(b);
                    }

                    break;

                case ReadState.SawIac:
                    if (b == Telnet.Iac)
                    {
                        _pendingData.Add(Telnet.Iac);
                        _state = ReadState.Data;
                    }
                    else if (b is Telnet.Will or Telnet.Wont or Telnet.Do or Telnet.Dont)
                    {
                        _pendingTelnetCommand = b;
                        _state = ReadState.SawCommand;
                    }
                    else if (b == Telnet.Sb)
                    {
                        _state = ReadState.SawSbWaitingOption;
                    }
                    else
                    {
                        // A stray SE, or a single-byte Telnet command (NOP/AYT/...) we don't act on.
                        _state = ReadState.Data;
                    }

                    break;

                case ReadState.SawCommand:
                    await HandleOptionNegotiationAsync(_pendingTelnetCommand, b, cancellationToken).ConfigureAwait(false);
                    _state = ReadState.Data;
                    break;

                case ReadState.SawSbWaitingOption:
                    _subnegotiationOption = b;
                    _subnegotiationBuffer.Clear();
                    _state = ReadState.InSubnegotiation;
                    break;

                case ReadState.InSubnegotiation:
                    if (b == Telnet.Iac)
                    {
                        _state = ReadState.InSubnegotiationSawIac;
                    }
                    else
                    {
                        _subnegotiationBuffer.Add(b);
                    }

                    break;

                case ReadState.InSubnegotiationSawIac:
                    if (b == Telnet.Iac)
                    {
                        // An escaped literal 0xFF inside the subnegotiation payload — keep both raw
                        // bytes; Rfc2217Codec.Unescape collapses the pair once the frame is complete.
                        _subnegotiationBuffer.Add(Telnet.Iac);
                        _subnegotiationBuffer.Add(Telnet.Iac);
                        _state = ReadState.InSubnegotiation;
                    }
                    else if (b == Telnet.Se)
                    {
                        HandleSubnegotiationComplete();
                        _state = ReadState.Data;
                    }
                    else
                    {
                        // Malformed: IAC inside a subnegotiation followed by neither IAC nor SE.
                        // Drop the in-progress frame rather than guessing at recovery.
                        _subnegotiationBuffer.Clear();
                        _state = ReadState.Data;
                    }

                    break;
            }
        }
    }

    private void HandleSubnegotiationComplete()
    {
        if (_subnegotiationOption == Telnet.ComPortOption)
        {
            var unescaped = Rfc2217Codec.Unescape(_subnegotiationBuffer.ToArray());
            var message = Rfc2217Codec.DecodeComPortMessage(unescaped);
            if (message is not null)
            {
                ComPortMessageReceived?.Invoke(message);
            }
        }

        _subnegotiationBuffer.Clear();
    }

    private async ValueTask HandleOptionNegotiationAsync(byte command, byte option, CancellationToken cancellationToken)
    {
        if (option == Telnet.ComPortOption)
        {
            var accepted = command is Telnet.Will or Telnet.Do;
            ComPortOptionNegotiationReceived?.Invoke(accepted);
            return;
        }

        // We don't support any other Telnet option. Telnet etiquette only requires an answer to
        // WILL/DO (an offer or a request) — WONT/DONT for an option we never enabled needs none.
        if (command == Telnet.Will)
        {
            await SendRawLockedAsync(new byte[] { Telnet.Iac, Telnet.Dont, option }, cancellationToken).ConfigureAwait(false);
        }
        else if (command == Telnet.Do)
        {
            await SendRawLockedAsync(new byte[] { Telnet.Iac, Telnet.Wont, option }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask SendRawLockedAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _inner.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
