using System.Buffers.Binary;
using System.Text;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>
/// Decodes one <see cref="FrameSchema"/> frame's bytes into named values (numbers formatted the way a text reply would
/// be, so the same expressions and displays read them). Never throws on wire data: a frame that is too short or whose
/// <see cref="FrameField.Expect"/> bytes differ simply doesn't decode.
/// </summary>
public sealed class FrameDecoder
{
    private readonly FrameSchema _schema;
    private readonly bool _bigEndian;
    private readonly byte[][] _expected;

    public FrameDecoder(FrameSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Validate() is { Count: > 0 } errors)
        {
            throw new ArgumentException("The frame schema isn't valid: " + string.Join(" ", errors), nameof(schema));
        }

        _schema = schema;
        _bigEndian = string.Equals(schema.Endian, "be", StringComparison.OrdinalIgnoreCase);
        Length = schema.Length!.Value;
        Sync = schema.SyncBytes();
        _expected = [.. schema.Fields.Select(f => FrameField.ParseHex(f.Expect))];
    }

    /// <summary>The frame's total length in bytes.</summary>
    public int Length { get; }

    /// <summary>The bytes every frame starts with (empty when the schema declares none).</summary>
    public byte[] Sync { get; }

    /// <summary>Decodes the first <see cref="Length"/> bytes of <paramref name="frame"/>; false when it is too short or an expected constant differs.</summary>
    public bool TryDecode(ReadOnlySpan<byte> frame, Dictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (frame.Length < Length)
        {
            return false;
        }

        var staged = new List<KeyValuePair<string, string>>();
        var at = 0;
        for (var i = 0; i < _schema.Fields.Count; i++)
        {
            var field = _schema.Fields[i];
            var size = field.EffectiveSize!.Value;
            var slice = frame.Slice(at, size);
            at += size;

            if (_expected[i].Length > 0 && !slice.SequenceEqual(_expected[i]))
            {
                return false;
            }

            if (field.Publishes)
            {
                staged.Add(new(field.Name, Format(field, slice)));
            }
        }

        foreach (var (name, text) in staged)
        {
            values[name] = text;
        }

        return true;
    }

    private string Format(FrameField field, ReadOnlySpan<byte> bytes)
    {
        if (!field.IsNumber)
        {
            return field.Type == "str"
                ? Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ')
                : Convert.ToHexString(bytes);
        }

        var big = field.Endian is { } endian ? string.Equals(endian, "be", StringComparison.OrdinalIgnoreCase) : _bigEndian;
        var raw = ReadNumber(field.Type, bytes, big);
        if (field.Scale is { } scale)
        {
            raw *= scale;
        }

        if (field.Offset is { } offset)
        {
            raw += offset;
        }

        return ChartValue.Format(raw);
    }

    private static double ReadNumber(string type, ReadOnlySpan<byte> b, bool big) => type switch
    {
        "u1" => b[0],
        "s1" => (sbyte)b[0],
        "u2" => big ? BinaryPrimitives.ReadUInt16BigEndian(b) : BinaryPrimitives.ReadUInt16LittleEndian(b),
        "s2" => big ? BinaryPrimitives.ReadInt16BigEndian(b) : BinaryPrimitives.ReadInt16LittleEndian(b),
        "u4" => big ? BinaryPrimitives.ReadUInt32BigEndian(b) : BinaryPrimitives.ReadUInt32LittleEndian(b),
        "s4" => big ? BinaryPrimitives.ReadInt32BigEndian(b) : BinaryPrimitives.ReadInt32LittleEndian(b),
        "u8" => big ? BinaryPrimitives.ReadUInt64BigEndian(b) : BinaryPrimitives.ReadUInt64LittleEndian(b),
        "s8" => big ? BinaryPrimitives.ReadInt64BigEndian(b) : BinaryPrimitives.ReadInt64LittleEndian(b),
        "f4" => big ? BinaryPrimitives.ReadSingleBigEndian(b) : BinaryPrimitives.ReadSingleLittleEndian(b),
        "f8" => big ? BinaryPrimitives.ReadDoubleBigEndian(b) : BinaryPrimitives.ReadDoubleLittleEndian(b),
        _ => double.NaN,
    };
}
