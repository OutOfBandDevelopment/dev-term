using System.Globalization;
using System.Text;

namespace DevTerm.Transports.Brokers;

/// <summary>One STOMP 1.2 frame: a command, headers and a body (https://stomp.github.io/stomp-specification-1.2.html).</summary>
internal sealed record StompFrame(string Command, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body)
{
    public string? Header(string name)
    {
        foreach (var header in Headers)
        {
            if (header.Key == name)
            {
                return header.Value;
            }
        }

        return null;
    }

    /// <summary>Wire form: <c>COMMAND\nname:value\n...\n\nbody\0</c>. CONNECT frames are never escaped, per the spec.</summary>
    public byte[] Encode()
    {
        var escape = Command is not ("CONNECT" or "STOMP");
        var head = new StringBuilder(Command).Append('\n');
        foreach (var (name, value) in Headers)
        {
            head.Append(escape ? Escape(name) : name).Append(':').Append(escape ? Escape(value) : value).Append('\n');
        }

        head.Append('\n');
        var prefix = Encoding.UTF8.GetBytes(head.ToString());
        var bytes = new byte[prefix.Length + Body.Length + 1];
        prefix.CopyTo(bytes, 0);
        Body.CopyTo(bytes, prefix.Length);
        return bytes;
    }

    internal static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal).Replace(":", "\\c", StringComparison.Ordinal);

    internal static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i == value.Length - 1)
            {
                result.Append(value[i]);
                continue;
            }

            i++;
            result.Append(value[i] switch { 'n' => '\n', 'r' => '\r', 'c' => ':', var other => other });
        }

        return result.ToString();
    }

    /// <summary>
    /// Takes one complete frame off the front of <paramref name="buffer"/> (leading heart-beat newlines skipped); returns
    /// null when more bytes are needed. <paramref name="consumed"/> is how many bytes the frame used, counting its NUL.
    /// </summary>
    public static StompFrame? TryParse(ReadOnlySpan<byte> buffer, out int consumed)
    {
        consumed = 0;
        var start = 0;
        while (start < buffer.Length && buffer[start] is (byte)'\n' or (byte)'\r')
        {
            start++;
        }

        var rest = buffer[start..];
        var headerEnd = rest.IndexOf("\n\n"u8);
        var headerLength = 2;
        var crlf = rest.IndexOf("\r\n\r\n"u8);
        if (crlf >= 0 && (headerEnd < 0 || crlf < headerEnd))
        {
            headerEnd = crlf;
            headerLength = 4;
        }

        if (headerEnd < 0)
        {
            return null;
        }

        var lines = Encoding.UTF8.GetString(rest[..headerEnd]).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var headers = new List<KeyValuePair<string, string>>();
        var unescape = lines[0] is not ("CONNECTED" or "CONNECT");
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var name = unescape ? Unescape(line[..colon]) : line[..colon];
            var value = unescape ? Unescape(line[(colon + 1)..]) : line[(colon + 1)..];
            // The first occurrence of a repeated header wins (spec 1.2).
            if (!headers.Any(h => h.Key == name))
            {
                headers.Add(new(name, value));
            }
        }

        var bodyStart = headerEnd + headerLength;
        int bodyLength;
        var declaredText = headers.FirstOrDefault(h => h.Key == "content-length").Value;
        if (declaredText is not null && int.TryParse(declaredText, NumberStyles.None, CultureInfo.InvariantCulture, out var declared))
        {
            if (rest.Length < bodyStart + declared + 1)
            {
                return null;
            }

            bodyLength = declared;
        }
        else
        {
            var nul = rest[bodyStart..].IndexOf((byte)0);
            if (nul < 0)
            {
                return null;
            }

            bodyLength = nul;
        }

        consumed = start + bodyStart + bodyLength + 1;
        return new StompFrame(lines[0], headers, rest.Slice(bodyStart, bodyLength).ToArray());
    }
}
