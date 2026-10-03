using System.Buffers.Binary;

namespace DevTerm.Transports.Vxi11;

/// <summary>
/// A minimal ONC-RPC (RFC 5531) client over one TCP stream: AUTH_NONE calls, record marking
/// (RFC 5531 section 11), one call at a time. Enough for the portmapper and the VXI-11 core channel.
/// </summary>
internal sealed class OncRpcClient(Stream stream)
{
    private const uint _callMessage = 0;
    private const uint _replyMessage = 1;
    private const uint _lastFragment = 0x80000000;
    private const int _maxReply = 1 << 20;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private uint _xid = (uint)Random.Shared.Next(1, int.MaxValue);

    public async Task<byte[]> CallAsync(uint program, uint version, uint procedure, byte[] arguments, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var xid = ++_xid;
            var call = new XdrWriter()
                .UInt32(xid).UInt32(_callMessage).UInt32(2).UInt32(program).UInt32(version).UInt32(procedure)
                .UInt32(0).UInt32(0) // credentials: AUTH_NONE, empty
                .UInt32(0).UInt32(0) // verifier: AUTH_NONE, empty
                .ToArray();

            var record = new byte[4 + call.Length + arguments.Length];
            BinaryPrimitives.WriteUInt32BigEndian(record, _lastFragment | (uint)(call.Length + arguments.Length));
            call.CopyTo(record, 4);
            arguments.CopyTo(record, 4 + call.Length);
            await stream.WriteAsync(record, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var reply = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            var reader = new XdrReader(reply);
            if (reader.UInt32() != xid || reader.UInt32() != _replyMessage)
            {
                throw new InvalidDataException("The RPC reply does not match the call.");
            }

            if (reader.UInt32() != 0)
            {
                throw new InvalidOperationException("The RPC call was denied by the server.");
            }

            _ = reader.UInt32(); // verifier flavor
            _ = reader.Opaque(); // verifier body
            var accepted = reader.UInt32();
            if (accepted != 0)
            {
                throw new InvalidOperationException($"The RPC call was not accepted (accept_stat {accepted}); the service or procedure is not available.");
            }

            return reply[(reply.Length - reader.Remaining)..];
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]> ReadRecordAsync(CancellationToken cancellationToken)
    {
        using var record = new MemoryStream();
        var header = new byte[4];
        while (true)
        {
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var marker = BinaryPrimitives.ReadUInt32BigEndian(header);
            var length = (int)(marker & ~_lastFragment);
            if (record.Length + length > _maxReply)
            {
                throw new InvalidDataException("The RPC reply is too large.");
            }

            var fragment = new byte[length];
            await stream.ReadExactlyAsync(fragment, cancellationToken).ConfigureAwait(false);
            record.Write(fragment);
            if ((marker & _lastFragment) != 0)
            {
                return record.ToArray();
            }
        }
    }
}
