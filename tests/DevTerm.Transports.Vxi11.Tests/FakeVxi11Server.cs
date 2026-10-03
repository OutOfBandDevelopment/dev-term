using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DevTerm.Transports.Vxi11.Tests;

/// <summary>
/// A real loopback ONC-RPC server speaking just enough portmapper + VXI-11 core channel for the
/// transport: answers GETPORT with the core port, create_link, device_write (stores the text and
/// queues a canned reply) and device_read (returns the queued reply, or error 15 when empty).
/// </summary>
internal sealed class FakeVxi11Server : IDisposable
{
    private readonly TcpListener _portmapper = new(IPAddress.Loopback, 0);
    private readonly TcpListener _core = new(IPAddress.Loopback, 0);
    private readonly Queue<byte[]> _replies = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();

    public FakeVxi11Server(bool endsWithoutLineFeed = false)
    {
        EndsWithoutLineFeed = endsWithoutLineFeed;
        _portmapper.Start();
        _core.Start();
        _ = Task.Run(() => AcceptAsync(_portmapper));
        _ = Task.Run(() => AcceptAsync(_core));
    }

    public bool EndsWithoutLineFeed { get; }

    public int PortmapperPort => ((IPEndPoint)_portmapper.LocalEndpoint).Port;

    public int CorePort => ((IPEndPoint)_core.LocalEndpoint).Port;

    public List<string> Written { get; } = [];

    public bool LinkDestroyed { get; private set; }

    public string? DeviceName { get; private set; }

    public void Dispose()
    {
        _cts.Cancel();
        _portmapper.Stop();
        _core.Stop();
    }

    private async Task AcceptAsync(TcpListener listener)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (Exception)
        {
            // listener stopped
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        var header = new byte[4];
        try
        {
            while (true)
            {
                await stream.ReadExactlyAsync(header, _cts.Token);
                var body = new byte[(int)(BinaryPrimitives.ReadUInt32BigEndian(header) & 0x7FFFFFFF)];
                await stream.ReadExactlyAsync(body, _cts.Token);

                var reader = new XdrReader(body);
                var xid = reader.UInt32();
                _ = reader.UInt32(); // CALL
                _ = reader.UInt32(); // rpcvers
                var program = reader.UInt32();
                _ = reader.UInt32(); // version
                var procedure = reader.UInt32();
                for (var i = 0; i < 4; i++)
                {
                    _ = reader.UInt32(); // credential and verifier (flavor, length 0)
                }

                var result = Handle(program, procedure, reader);
                var reply = new XdrWriter().UInt32(xid).UInt32(1).UInt32(0).UInt32(0).UInt32(0).UInt32(0).ToArray();
                var record = new byte[4 + reply.Length + result.Length];
                BinaryPrimitives.WriteUInt32BigEndian(record, 0x80000000 | (uint)(reply.Length + result.Length));
                reply.CopyTo(record, 4);
                result.CopyTo(record, 4 + reply.Length);
                await stream.WriteAsync(record, _cts.Token);
            }
        }
        catch (Exception)
        {
            // client hung up
        }
    }

    private byte[] Handle(uint program, uint procedure, XdrReader args)
    {
        if (program == Vxi11Transport.PortmapperProgram)
        {
            return new XdrWriter().UInt32((uint)CorePort).ToArray();
        }

        switch (procedure)
        {
            case 10:
                _ = args.UInt32();
                _ = args.UInt32();
                _ = args.UInt32();
                DeviceName = Encoding.ASCII.GetString(args.Opaque());
                return new XdrWriter().UInt32(0).UInt32(77).UInt32(1234).UInt32(8192).ToArray();
            case 11:
                _ = args.UInt32();
                _ = args.UInt32();
                _ = args.UInt32();
                _ = args.UInt32();
                var text = Encoding.ASCII.GetString(args.Opaque());
                lock (_lock)
                {
                    Written.Add(text);
                    var answer = text.TrimEnd('\n') == "*IDN?" ? "FAKE,MODEL,1,2.0" : text.TrimEnd('\n');
                    _replies.Enqueue(Encoding.ASCII.GetBytes(EndsWithoutLineFeed ? answer : answer + "\n"));
                }

                return new XdrWriter().UInt32(0).UInt32((uint)text.Length).ToArray();
            case 12:
                lock (_lock)
                {
                    if (_replies.TryDequeue(out var reply))
                    {
                        return new XdrWriter().UInt32(0).UInt32(4).Opaque(reply).ToArray();
                    }
                }

                return new XdrWriter().UInt32(15).UInt32(0).Opaque([]).ToArray();
            case 23:
                LinkDestroyed = true;
                return new XdrWriter().UInt32(0).ToArray();
            default:
                return new XdrWriter().UInt32(8).ToArray();
        }
    }
}
