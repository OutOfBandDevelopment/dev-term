using System.Buffers.Binary;
using System.Text;

namespace DevTerm.Configuration.Discovery;

/// <summary>One resource record from a DNS answer, decoded for the types DNS-SD needs.</summary>
/// <param name="Name">The owner name.</param>
/// <param name="Type">1 A, 12 PTR, 16 TXT, 33 SRV.</param>
/// <param name="Target">PTR target, SRV target host, or the dotted address for an A record.</param>
/// <param name="Port">The SRV port, otherwise 0.</param>
public sealed record DnsRecord(string Name, int Type, string Target, int Port);

/// <summary>The few bytes of DNS (RFC 1035) mDNS service discovery needs: build a PTR query, decode the answer. Never throws on malformed input.</summary>
public static class DnsMessage
{
    public const int TypeA = 1;
    public const int TypePtr = 12;
    public const int TypeTxt = 16;
    public const int TypeSrv = 33;

    /// <summary>A query for the PTR records of each name, asking for a unicast reply (the QU bit) so an ordinary socket receives it.</summary>
    public static byte[] BuildPtrQuery(IEnumerable<string> names)
    {
        var list = names.ToList();
        var bytes = new List<byte> { 0, 0, 0, 0, (byte)(list.Count >> 8), (byte)list.Count, 0, 0, 0, 0, 0, 0 };
        foreach (var name in list)
        {
            foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var label8 = Encoding.UTF8.GetBytes(label);
                bytes.Add((byte)label8.Length);
                bytes.AddRange(label8);
            }

            bytes.Add(0);
            bytes.AddRange(new byte[] { 0, TypePtr, 0x80, 0x01 });
        }

        return [.. bytes];
    }

    /// <summary>Decodes the answer and additional records. Returns what could be read before any malformed part.</summary>
    public static IReadOnlyList<DnsRecord> ParseRecords(ReadOnlySpan<byte> message)
    {
        var records = new List<DnsRecord>();
        if (message.Length < 12)
        {
            return records;
        }

        var questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
        var total = BinaryPrimitives.ReadUInt16BigEndian(message[6..]) + BinaryPrimitives.ReadUInt16BigEndian(message[8..]) + BinaryPrimitives.ReadUInt16BigEndian(message[10..]);
        var offset = 12;
        for (var i = 0; i < questions; i++)
        {
            if (ReadName(message, ref offset) is null || offset + 4 > message.Length)
            {
                return records;
            }

            offset += 4;
        }

        for (var i = 0; i < total; i++)
        {
            if (ReadName(message, ref offset) is not { } name || offset + 10 > message.Length)
            {
                break;
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 8)..]);
            offset += 10;
            if (offset + length > message.Length)
            {
                break;
            }

            var data = offset;
            if (type == TypePtr && ReadName(message, ref data) is { } target)
            {
                records.Add(new DnsRecord(name, type, target, 0));
            }
            else if (type == TypeSrv && length >= 7)
            {
                var port = BinaryPrimitives.ReadUInt16BigEndian(message[(data + 4)..]);
                data += 6;
                if (ReadName(message, ref data) is { } host)
                {
                    records.Add(new DnsRecord(name, type, host, port));
                }
            }
            else if (type == TypeA && length == 4)
            {
                records.Add(new DnsRecord(name, type, string.Join('.', message.Slice(data, 4).ToArray()), 0));
            }

            offset += length;
        }

        return records;
    }

    private static string? ReadName(ReadOnlySpan<byte> message, ref int offset)
    {
        var labels = new List<string>();
        var position = offset;
        var resumeAt = -1;
        for (var jumps = 0; jumps < 64; jumps++)
        {
            if (position >= message.Length)
            {
                return null;
            }

            var length = message[position];
            if (length == 0)
            {
                offset = resumeAt >= 0 ? resumeAt : position + 1;
                return string.Join('.', labels);
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (position + 1 >= message.Length)
                {
                    return null;
                }

                resumeAt = resumeAt >= 0 ? resumeAt : position + 2;
                position = ((length & 0x3F) << 8) | message[position + 1];
                continue;
            }

            if (position + 1 + length > message.Length)
            {
                return null;
            }

            labels.Add(Encoding.UTF8.GetString(message.Slice(position + 1, length)));
            position += 1 + length;
        }

        return null;
    }
}
