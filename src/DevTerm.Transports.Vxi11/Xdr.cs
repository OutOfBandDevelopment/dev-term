using System.Buffers.Binary;
using System.Text;

namespace DevTerm.Transports.Vxi11;

/// <summary>Writes XDR (RFC 4506) items: 4-byte big-endian integers and opaque/string data padded to a multiple of four.</summary>
internal sealed class XdrWriter
{
    private readonly List<byte> _bytes = [];

    public XdrWriter UInt32(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        _bytes.AddRange(buffer);
        return this;
    }

    public XdrWriter Bool(bool value) => UInt32(value ? 1u : 0u);

    public XdrWriter Opaque(ReadOnlySpan<byte> data)
    {
        UInt32((uint)data.Length);
        _bytes.AddRange(data.ToArray());
        for (var i = 0; i < Padding(data.Length); i++)
        {
            _bytes.Add(0);
        }

        return this;
    }

    public XdrWriter String(string value) => Opaque(Encoding.ASCII.GetBytes(value));

    public byte[] ToArray() => [.. _bytes];

    internal static int Padding(int length) => (4 - (length % 4)) % 4;
}

/// <summary>Reads XDR items; throws <see cref="InvalidDataException"/> on a short or oversized buffer instead of reading past the end.</summary>
internal sealed class XdrReader(byte[] buffer)
{
    private int _offset;

    public int Remaining => buffer.Length - _offset;

    public uint UInt32()
    {
        if (Remaining < 4)
        {
            throw new InvalidDataException("The RPC reply is shorter than expected.");
        }

        var value = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(_offset, 4));
        _offset += 4;
        return value;
    }

    public byte[] Opaque()
    {
        var length = UInt32();
        if (length > int.MaxValue || length + XdrWriter.Padding((int)length) > Remaining)
        {
            throw new InvalidDataException("The RPC reply carries more data than it holds.");
        }

        var data = buffer.AsSpan(_offset, (int)length).ToArray();
        _offset += (int)length + XdrWriter.Padding((int)length);
        return data;
    }
}
