using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DevTerm.Logging;

/// <summary>
/// Converts headers and records to and from their one-line JSON form. The file layout (one JSON
/// object per line, header first) is documented in docs/design/session-logging.md; this class is
/// the single place that layout is encoded, used by both <see cref="SessionLogWriter"/> (streaming,
/// while capturing) and <see cref="SessionLog"/> (load/save, trim, markup).
/// </summary>
public static class SessionLogFormat
{
    /// <summary>The file extension new logs get (JSON Lines).</summary>
    public const string FileExtension = ".jsonl";

    private const string _timestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        // Keeps note text/connection strings readable in the file (no \u escaping of every
        // non-ASCII character) while still escaping what JSON requires.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString(_timestampFormat, CultureInfo.InvariantCulture);

    public static string WriteHeader(SessionLogHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return WriteLine(writer =>
        {
            writer.WriteString("type", "header");
            writer.WriteString("format", SessionLogHeader.FormatName);
            writer.WriteNumber("version", header.Version);
            writer.WriteString("created", FormatTimestamp(header.Created));
            WriteOptional(writer, "application", header.Application);
            WriteOptional(writer, "connection", header.Connection);
            WriteOptional(writer, "profile", header.Profile);
            WriteOptional(writer, "transport", header.Transport);
            writer.WriteStartArray("presenters");
            foreach (var presenter in header.Presenters)
            {
                writer.WriteStringValue(presenter);
            }

            writer.WriteEndArray();
            WriteOptional(writer, "parser", header.Parser);
            WriteOptional(writer, "trimmedFrom", header.TrimmedFrom);
        });
    }

    public static string WriteRecord(SessionLogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Kind == SessionLogRecordKind.Unknown)
        {
            return record.RawJson ?? throw new ArgumentException("An unknown record has no raw JSON to write back.", nameof(record));
        }

        return WriteLine(writer =>
        {
            writer.WriteString("type", TypeName(record.Kind));
            if (record.Sequence is { } sequence)
            {
                writer.WriteNumber("seq", sequence);
            }

            writer.WriteString("t", FormatTimestamp(record.Timestamp));
            switch (record.Kind)
            {
                case SessionLogRecordKind.Tx:
                case SessionLogRecordKind.Rx:
                    writer.WriteBase64String("data", record.Data.Span);
                    break;
                case SessionLogRecordKind.Disconnect:
                    WriteOptional(writer, "error", record.Text);
                    break;
                case SessionLogRecordKind.Note:
                    writer.WriteString("text", record.Text ?? string.Empty);
                    break;
                case SessionLogRecordKind.Session:
                    WriteOptional(writer, "connection", record.Connection);
                    WriteOptional(writer, "profile", record.Profile);
                    WriteOptional(writer, "state", record.State);
                    break;
            }
        });
    }

    /// <exception cref="SessionLogFormatException">Not a dev-term session log header, or one written by a newer, incompatible version.</exception>
    public static SessionLogHeader ReadHeader(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            throw new SessionLogFormatException($"Not a dev-term session log: the first line isn't JSON ({ex.Message}).", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || GetString(root, "type") != "header"
                || GetString(root, "format") != SessionLogHeader.FormatName)
            {
                throw new SessionLogFormatException($"Not a dev-term session log: the first line isn't a \"{SessionLogHeader.FormatName}\" header.");
            }

            var version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out var parsed) ? parsed : 0;
            if (version < 1 || version > SessionLogHeader.CurrentVersion)
            {
                throw new SessionLogFormatException(
                    $"This log is format version {version}; this dev-term reads versions 1 to {SessionLogHeader.CurrentVersion}.");
            }

            var presenters = new List<string>();
            if (root.TryGetProperty("presenters", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                presenters.AddRange(list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
            }

            return new SessionLogHeader
            {
                Version = version,
                Created = ParseTimestamp(GetString(root, "created"), "created"),
                Application = GetString(root, "application"),
                Connection = GetString(root, "connection"),
                Profile = GetString(root, "profile"),
                Transport = GetString(root, "transport"),
                Presenters = presenters,
                Parser = GetString(root, "parser"),
                TrimmedFrom = GetString(root, "trimmedFrom"),
            };
        }
    }

    /// <exception cref="SessionLogFormatException">The line isn't a valid record.</exception>
    public static SessionLogRecord ReadRecord(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SessionLogFormatException("A record must be a JSON object.");
            }

            var type = GetString(root, "type") ?? throw new SessionLogFormatException("A record has no \"type\".");
            var kind = KindFor(type);
            if (kind == SessionLogRecordKind.Unknown)
            {
                return new SessionLogRecord
                {
                    Kind = SessionLogRecordKind.Unknown,
                    Timestamp = GetString(root, "t") is { } t ? ParseTimestamp(t, "t") : DateTimeOffset.MinValue,
                    RawJson = line,
                };
            }

            long? sequence = root.TryGetProperty("seq", out var seq) && seq.TryGetInt64(out var s) ? s : null;
            var timestamp = ParseTimestamp(GetString(root, "t"), "t");
            ReadOnlyMemory<byte> data = default;
            if (kind is SessionLogRecordKind.Tx or SessionLogRecordKind.Rx)
            {
                if (!root.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.String || !d.TryGetBytesFromBase64(out var bytes))
                {
                    throw new SessionLogFormatException($"A \"{type}\" record needs base64 \"data\".");
                }

                data = bytes;
            }

            return new SessionLogRecord
            {
                Kind = kind,
                Sequence = sequence,
                Timestamp = timestamp,
                Data = data,
                Text = kind switch
                {
                    SessionLogRecordKind.Note => GetString(root, "text") ?? string.Empty,
                    SessionLogRecordKind.Disconnect => GetString(root, "error"),
                    _ => null,
                },
                Connection = GetString(root, "connection"),
                Profile = GetString(root, "profile"),
                State = GetString(root, "state"),
            };
        }
        catch (JsonException ex)
        {
            throw new SessionLogFormatException($"Not valid JSON: {ex.Message}", ex);
        }
    }

    internal static string TypeName(SessionLogRecordKind kind) => kind switch
    {
        SessionLogRecordKind.Session => "session",
        SessionLogRecordKind.Open => "open",
        SessionLogRecordKind.Close => "close",
        SessionLogRecordKind.Disconnect => "disconnect",
        SessionLogRecordKind.Tx => "tx",
        SessionLogRecordKind.Rx => "rx",
        SessionLogRecordKind.Note => "note",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An unknown record keeps its own raw type."),
    };

    private static SessionLogRecordKind KindFor(string type) => type switch
    {
        "session" => SessionLogRecordKind.Session,
        "open" => SessionLogRecordKind.Open,
        "close" => SessionLogRecordKind.Close,
        "disconnect" => SessionLogRecordKind.Disconnect,
        "tx" => SessionLogRecordKind.Tx,
        "rx" => SessionLogRecordKind.Rx,
        "note" => SessionLogRecordKind.Note,
        _ => SessionLogRecordKind.Unknown,
    };

    private static string WriteLine(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset ParseTimestamp(string? value, string field)
    {
        if (value is not null
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        throw new SessionLogFormatException($"\"{field}\" must be an ISO 8601 UTC timestamp, got '{value}'.");
    }
}

/// <summary>A file (or one of its lines) isn't a valid dev-term session log.</summary>
public sealed class SessionLogFormatException : Exception
{
    public SessionLogFormatException(string message)
        : base(message)
    {
    }

    public SessionLogFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
