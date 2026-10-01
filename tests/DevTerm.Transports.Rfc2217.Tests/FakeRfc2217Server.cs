using System.Buffers;
using System.IO.Pipelines;
using DevTerm.Transports.Tcp;
using Moq;

namespace DevTerm.Transports.Rfc2217.Tests;

/// <summary>
/// Stands in for the remote RFC 2217 server side of the wire: a <see cref="Mock{ITcpConnection}"/>
/// whose <see cref="ITcpConnection.Stream"/> is a real, in-memory <see cref="DuplexPipeStream"/>.
/// Tests call the <c>Send*</c> helpers to play "bytes arrived from the server" and
/// <see cref="WaitForNextWriteAsync"/>/<see cref="DrainPendingOutgoing"/> to inspect what
/// <see cref="Rfc2217Transport"/> wrote back.
/// </summary>
internal sealed class FakeRfc2217Server
{
    private readonly Pipe _incoming;
    private readonly Pipe _outgoing;

    public FakeRfc2217Server(long outgoingPauseWriterThreshold = 65536)
    {
        _incoming = new Pipe();
        _outgoing = new Pipe(new PipeOptions(pauseWriterThreshold: outgoingPauseWriterThreshold, resumeWriterThreshold: Math.Max(1, outgoingPauseWriterThreshold / 2)));

        Connection = new Mock<ITcpConnection>();
        Connection.SetupGet(c => c.Stream).Returns(new DuplexPipeStream(_incoming, _outgoing));
    }

    public Mock<ITcpConnection> Connection { get; }

    public Task SendWillDoComPortOptionAsync(CancellationToken cancellationToken) =>
        WriteIncomingAsync(Telnet.BuildWillDo(Telnet.ComPortOption), cancellationToken);

    public Task SendWontDontComPortOptionAsync(CancellationToken cancellationToken) =>
        WriteIncomingAsync([Telnet.Iac, Telnet.Wont, Telnet.ComPortOption, Telnet.Iac, Telnet.Dont, Telnet.ComPortOption], cancellationToken);

    public Task SendNotifyLineStateAsync(Rfc2217LineState lineState, CancellationToken cancellationToken) =>
        WriteIncomingAsync(Rfc2217Codec.EncodeSubnegotiation(Rfc2217Command.NotifyLineState, [(byte)lineState]), cancellationToken);

    public Task SendDataAsync(byte[] data, CancellationToken cancellationToken) =>
        WriteIncomingAsync(Rfc2217Codec.Escape(data), cancellationToken);

    public Task CompleteIncomingAsync() => _incoming.Writer.CompleteAsync().AsTask();

    /// <summary>Drains whatever is already sitting in the outgoing pipe (non-blocking) and discards it, resetting the boundary for the next <see cref="WaitForNextWriteAsync"/>.</summary>
    public void DrainPendingOutgoing()
    {
        if (_outgoing.Reader.TryRead(out var result))
        {
            _outgoing.Reader.AdvanceTo(result.Buffer.End);
        }
    }

    /// <summary>Returns whatever bytes have been written (and not yet consumed) to the outgoing side, waiting for at least one byte if none have arrived yet.</summary>
    public async Task<byte[]> WaitForNextWriteAsync(CancellationToken cancellationToken)
    {
        var result = await _outgoing.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var bytes = result.Buffer.ToArray();
        _outgoing.Reader.AdvanceTo(result.Buffer.End);
        return bytes;
    }

    /// <summary>Continuously reads and discards the outgoing side until canceled — a sink that stands in for "the remote peer keeps draining normally" while a test needs <see cref="Rfc2217Transport.OpenAsync"/> to complete against a small-threshold pipe before deliberately starving a later write.</summary>
    public async Task DrainOutgoingContinuouslyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var result = await _outgoing.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                _outgoing.Reader.AdvanceTo(result.Buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WriteIncomingAsync(byte[] bytes, CancellationToken cancellationToken) =>
        await _incoming.Writer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
}
