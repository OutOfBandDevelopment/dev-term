using System.Buffers.Binary;
using System.Text;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests;

/// <summary>What <see cref="FrameDecoder.Probe"/> found at the start of a buffer.</summary>
public enum FrameProbe
{
    /// <summary>A whole frame's length is known and the buffer holds it.</summary>
    Ready,

    /// <summary>More bytes are needed before the frame's length (or the frame itself) is known.</summary>
    NeedMore,

    /// <summary>The bytes can't start a frame (a length outside what the schema allows): drop a byte and look again.</summary>
    Invalid,
}

/// <summary>
/// Decodes one <see cref="FrameSchema"/> frame's bytes into named values (numbers formatted the way a text reply would
/// be, so the same expressions and displays read them). Never throws on wire data: a frame that is too short, whose
/// <see cref="FrameField.Expect"/> bytes differ, or whose checksum fails simply doesn't decode.
/// </summary>
public sealed class FrameDecoder
{
    // A length field must not make the presenter wait for an absurd frame.
    private const int _maxFrame = 64 * 1024;

    private readonly FrameSchema _schema;
    private readonly FrameLayout _layout;
    private readonly bool _bigEndian;
    private readonly byte[][] _expected;
    private readonly FrameSlot? _lengthSlot;

    public FrameDecoder(FrameSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Validate() is { Count: > 0 } errors)
        {
            throw new ArgumentException("The frame schema isn't valid: " + string.Join(" ", errors), nameof(schema));
        }

        _schema = schema;
        _layout = schema.Layout()!;
        _bigEndian = string.Equals(schema.Endian, "be", StringComparison.OrdinalIgnoreCase);
        Length = _layout.MinLength;
        IsVariable = _layout.TailIndex >= 0;
        Sync = schema.SyncBytes();
        _expected = [.. schema.Fields.Select(f => FrameField.ParseHex(f.Expect))];
        if (IsVariable)
        {
            _lengthSlot = _layout.Slots.First(slot => string.Equals(slot.Field.Name, schema.LengthField, StringComparison.Ordinal));
        }
    }

    /// <summary>The frame's total length in bytes; for a variable-length frame, the shortest it can be (see <see cref="Probe"/>).</summary>
    public int Length { get; }

    /// <summary>True when the frame's length comes from a field in it.</summary>
    public bool IsVariable { get; }

    /// <summary>The bytes every frame starts with (empty when the schema declares none).</summary>
    public byte[] Sync { get; }

    /// <summary>
    /// Works out how long the frame at the start of <paramref name="buffer"/> is. A fixed frame is always <see cref="Length"/>;
    /// a variable one reads its length field, and says <see cref="FrameProbe.NeedMore"/> until that field and the whole frame have arrived.
    /// </summary>
    public FrameProbe Probe(ReadOnlySpan<byte> buffer, out int total)
    {
        total = Length;
        if (!IsVariable)
        {
            return buffer.Length >= Length ? FrameProbe.Ready : FrameProbe.NeedMore;
        }

        var slot = _lengthSlot!;
        var needed = (slot.StartBit + slot.BitLength + 7) / 8;
        if (buffer.Length < needed)
        {
            return FrameProbe.NeedMore;
        }

        var raw = slot.Field.BitWidth > 0
            ? ReadBits(buffer, slot)
            : ReadNumber(slot.Field.Type, buffer.Slice(slot.StartByte, slot.BitLength / 8), EndianOf(slot.Field));
        var wanted = raw + _schema.LengthAdjust;

        if (wanted < Length || wanted > _maxFrame)
        {
            return FrameProbe.Invalid;
        }

        total = (int)wanted;
        return buffer.Length >= total ? FrameProbe.Ready : FrameProbe.NeedMore;
    }

    /// <summary>
    /// Decodes one frame. A fixed frame reads the first <see cref="Length"/> bytes of <paramref name="frame"/>; a variable one
    /// takes <paramref name="frame"/> as the whole frame (its length from <see cref="Probe"/>). False when it is too short,
    /// an expected constant differs, or the checksum fails.
    /// </summary>
    public bool TryDecode(ReadOnlySpan<byte> frame, Dictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (frame.Length < Length)
        {
            return false;
        }

        if (!IsVariable)
        {
            frame = frame[..Length];
        }

        if (_schema.Checksum is { } checksum && !checksum.Matches(frame))
        {
            return false;
        }

        var staged = new List<KeyValuePair<string, string>>();
        for (var i = 0; i < _layout.Slots.Count; i++)
        {
            var slot = _layout.Slots[i];
            var field = slot.Field;
            var bytes = i == _layout.TailIndex
                ? frame[slot.StartByte..(frame.Length - _layout.ChecksumBytes)]
                : field.BitWidth > 0 ? default : frame.Slice(slot.StartByte, slot.BitLength / 8);

            if (_expected[i].Length > 0 && !bytes.SequenceEqual(_expected[i]))
            {
                return false;
            }

            if (field.Publishes)
            {
                staged.Add(new(field.Name, field.BitWidth > 0 ? FormatNumber(field, ReadBits(frame, slot)) : Format(field, bytes)));
            }
        }

        foreach (var (name, text) in staged)
        {
            values[name] = text;
        }

        return true;
    }

    /// <summary>A bit field's unsigned value, read MSB first from the bit position it starts at.</summary>
    private static double ReadBits(ReadOnlySpan<byte> frame, FrameSlot slot)
    {
        ulong value = 0;
        for (var bit = slot.StartBit; bit < slot.StartBit + slot.BitLength; bit++)
        {
            value = (value << 1) | (uint)((frame[bit / 8] >> (7 - (bit % 8))) & 1);
        }

        return value;
    }

    private bool EndianOf(FrameField field) =>
        field.Endian is { } endian ? string.Equals(endian, "be", StringComparison.OrdinalIgnoreCase) : _bigEndian;

    private string Format(FrameField field, ReadOnlySpan<byte> bytes)
    {
        if (!field.IsNumber)
        {
            return field.Type == "str"
                ? Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ')
                : Convert.ToHexString(bytes);
        }

        return FormatNumber(field, ReadNumber(field.Type, bytes, EndianOf(field)));
    }

    private static string FormatNumber(FrameField field, double raw)
    {
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
