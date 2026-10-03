using System.Globalization;
using System.Text.Json.Serialization;

namespace DevTerm.DeviceManifests;

/// <summary>
/// A fixed-layout binary frame a device sends: an optional sync prefix, then fields at consecutive offsets. Each field
/// publishes a live value under its <see cref="FrameField.Name"/> (dotted names like <c>header.length</c> are fine), the
/// binary counterpart to <see cref="InboundProtocol.Patterns"/>' regex captures. The <c>.ksy</c> importer
/// (<see cref="KsyImporter"/>) generates one of these from a Kaitai Struct file. See docs/design/features/ksy-importer.md.
/// </summary>
public sealed class FrameSchema
{
    /// <summary>Hex bytes (<c>"A5 5A"</c>) every frame starts with; the decoder finds frames by them and re-syncs after garbage. Unset means frames are back to back from the first byte.</summary>
    public string? Sync { get; set; }

    /// <summary><c>le</c> (the default) or <c>be</c>: the byte order of multi-byte numbers a field doesn't override.</summary>
    public string Endian { get; set; } = "le";

    public List<FrameField> Fields { get; set; } = [];

    /// <summary>
    /// Makes the frame variable-length: the name of a numeric field (<c>u1 u2 u4</c> or a bit field) holding a length. The
    /// last field must then be a <c>bytes</c> or <c>str</c> with no <see cref="FrameField.Size"/>; it takes whatever is left
    /// of the frame length (this field's value plus <see cref="LengthAdjust"/>) after the fixed fields and any checksum.
    /// </summary>
    public string? LengthField { get; set; }

    /// <summary>Added to the <see cref="LengthField"/> value to get the whole frame's length in bytes: 0 when it already counts everything, or the number of bytes it doesn't count (a length of the payload alone needs the header size here).</summary>
    public int LengthAdjust { get; set; }

    /// <summary>A checksum or CRC the frame ends with; a frame where it doesn't match is discarded.</summary>
    public FrameChecksum? Checksum { get; set; }

    /// <summary>The whole frame's length in bytes, or null when it varies (<see cref="IsVariable"/>) or a field's size is unknown (<see cref="Validate"/> reports why).</summary>
    [JsonIgnore]
    public int? Length => IsVariable ? null : Layout()?.MinLength;

    /// <summary>True when the last field takes its size from <see cref="LengthField"/>.</summary>
    [JsonIgnore]
    public bool IsVariable => Layout() is { TailIndex: >= 0 };

    /// <summary>The shortest frame in bytes (the whole length for a fixed frame), or null when a field's size is unknown.</summary>
    [JsonIgnore]
    public int? MinLength => Layout()?.MinLength;

    /// <summary>Where each field sits: bit fields pack MSB first, a byte-sized field starts on the next byte boundary. Null when a field's size is unknown.</summary>
    internal FrameLayout? Layout()
    {
        var slots = new List<FrameSlot>(Fields.Count);
        var cursor = 0;
        var tail = -1;
        for (var i = 0; i < Fields.Count; i++)
        {
            var field = Fields[i];
            if (field.BitWidth > 0)
            {
                slots.Add(new FrameSlot(field, cursor, field.BitWidth));
                cursor += field.BitWidth;
                continue;
            }

            cursor = (cursor + 7) / 8 * 8;
            if (field.EffectiveSize is { } size)
            {
                slots.Add(new FrameSlot(field, cursor, size * 8));
                cursor += size * 8;
            }
            else if (i == Fields.Count - 1 && field.Type is "bytes" or "str" && !string.IsNullOrWhiteSpace(LengthField))
            {
                tail = i;
                slots.Add(new FrameSlot(field, cursor, 0));
            }
            else
            {
                return null;
            }
        }

        var checksumBytes = Checksum is { } checksum && FrameChecksum.SizeOf(checksum.Kind) is { } checksumSize ? checksumSize : 0;
        return new FrameLayout(slots, (cursor + 7) / 8, tail, checksumBytes);
    }

    /// <summary>The sync prefix as bytes, or empty. Throws <see cref="FormatException"/> for text that isn't hex.</summary>
    public byte[] SyncBytes() => FrameField.ParseHex(Sync);

    /// <summary>What is wrong with this schema: empty when it can decode frames.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!string.Equals(Endian, "le", StringComparison.OrdinalIgnoreCase) && !string.Equals(Endian, "be", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Frame endian '{Endian}' must be 'le' or 'be'.");
        }

        try
        {
            SyncBytes();
        }
        catch (FormatException)
        {
            errors.Add($"Frame sync '{Sync}' isn't hex bytes (for example \"A5 5A\").");
        }

        if (Fields.Count == 0)
        {
            errors.Add("The frame has no fields.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Fields.Count; i++)
        {
            var field = Fields[i];
            errors.AddRange(field.Validate(openTail: i == Fields.Count - 1 && !string.IsNullOrWhiteSpace(LengthField)));
            if (field.Publishes && !string.IsNullOrWhiteSpace(field.Name) && !names.Add(field.Name))
            {
                errors.Add($"Frame field '{field.Name}' appears more than once.");
            }
        }

        if (!string.IsNullOrWhiteSpace(LengthField))
        {
            var target = Fields.FirstOrDefault(f => string.Equals(f.Name, LengthField, StringComparison.Ordinal));
            if (target is null || !target.IsNumber || target.Type is "f4" or "f8")
            {
                errors.Add($"Frame LengthField '{LengthField}' must name an integer field (u1 u2 u4 u8 s1 s2 s4 s8 or a bit field).");
            }
            else if (Fields.Count > 0 && ReferenceEquals(target, Fields[^1]))
            {
                errors.Add($"Frame LengthField '{LengthField}' must come before the variable-size last field.");
            }

            if (Fields.Count > 0 && Fields[^1] is { IsNumber: false } last && last.Type is not ("bytes" or "str"))
            {
                errors.Add("A variable-length frame must end with a 'bytes' or 'str' field with no Size.");
            }
        }

        if (Checksum is { } checksum)
        {
            errors.AddRange(checksum.Validate());
        }

        if (errors.Count == 0 && Checksum is { } sum && Layout() is { } layout && sum.Start > layout.FixedBytes)
        {
            errors.Add($"Frame checksum Start {sum.Start} is past the end of the fixed fields.");
        }

        return errors;
    }
}

/// <summary>One field of a <see cref="FrameSchema"/>: where it sits is implied by the fields before it.</summary>
public sealed class FrameField
{
    private static readonly Dictionary<string, int> _fixedSizes = new(StringComparer.Ordinal)
    {
        ["u1"] = 1,
        ["s1"] = 1,
        ["u2"] = 2,
        ["s2"] = 2,
        ["u4"] = 4,
        ["s4"] = 4,
        ["f4"] = 4,
        ["u8"] = 8,
        ["s8"] = 8,
        ["f8"] = 8,
    };

    /// <summary>The published value id; unused for a <c>skip</c> field.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>u1 u2 u4 u8 s1 s2 s4 s8 f4 f8</c> (numbers), <c>b1</c> to <c>b64</c> (a bit field, packed MSB first), <c>str</c> (ASCII text, trailing NULs and spaces dropped), <c>bytes</c> (published as hex) or <c>skip</c> (not published).</summary>
    public string Type { get; set; } = "u1";

    /// <summary><c>le</c> or <c>be</c> for this field only; the frame's endian when unset.</summary>
    public string? Endian { get; set; }

    /// <summary>Length in bytes, required for <c>str</c>, <c>bytes</c> and <c>skip</c>.</summary>
    public int? Size { get; set; }

    /// <summary>A number's raw value is multiplied by this (then <see cref="Offset"/> is added): a 16-bit value in tenths of a volt has Scale 0.1.</summary>
    public double? Scale { get; set; }

    public double? Offset { get; set; }

    public string? Unit { get; set; }

    /// <summary>What the picker and a generated panel call it; the name when unset.</summary>
    public string? Label { get; set; }

    /// <summary>A declared range (after scaling), used for sample data and a generated gauge.</summary>
    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    /// <summary>Hex bytes this field must equal (a magic number, a fixed command byte); a frame where it differs is discarded and the decoder re-syncs.</summary>
    public string? Expect { get; set; }

    [JsonIgnore]
    public bool Publishes => !string.Equals(Type, "skip", StringComparison.Ordinal);

    /// <summary>The width in bits when <see cref="Type"/> is a bit field (<c>b1</c> to <c>b64</c>, an unsigned value read MSB first), else 0.</summary>
    [JsonIgnore]
    public int BitWidth => Type.Length is > 1 and <= 3 && Type[0] == 'b' && int.TryParse(Type.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var width) && width is >= 1 and <= 64 ? width : 0;

    [JsonIgnore]
    public bool IsNumber => _fixedSizes.ContainsKey(Type) || BitWidth > 0;

    [JsonIgnore]
    public int? EffectiveSize => BitWidth > 0 ? null : _fixedSizes.TryGetValue(Type, out var size) ? size : Size is > 0 ? Size : null;

    internal static byte[] ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var digits = text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (digits.Length % 2 != 0)
        {
            throw new FormatException("Hex needs an even number of digits.");
        }

        return Convert.FromHexString(digits);
    }

    internal IEnumerable<string> Validate(bool openTail = false)
    {
        var which = string.IsNullOrWhiteSpace(Name) ? $"A '{Type}' frame field" : $"Frame field '{Name}'";
        if (Publishes && string.IsNullOrWhiteSpace(Name))
        {
            yield return $"{which} needs a name.";
        }

        if (!IsNumber && Type is not ("str" or "bytes" or "skip"))
        {
            yield return $"{which} has an unknown type '{Type}' (u1 u2 u4 u8 s1 s2 s4 s8 f4 f8 b1..b64 str bytes skip).";
        }
        else if (!IsNumber && Size is not > 0 && !(openTail && Type is "bytes" or "str"))
        {
            yield return $"{which} is '{Type}' and needs a Size.";
        }

        if (BitWidth > 0 && Expect is { Length: > 0 })
        {
            yield return $"{which} is a bit field; Expect works on whole-byte fields only.";
        }

        if (Endian is not null && !string.Equals(Endian, "le", StringComparison.OrdinalIgnoreCase) && !string.Equals(Endian, "be", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{which} endian '{Endian}' must be 'le' or 'be'.";
        }

        if (Expect is { Length: > 0 })
        {
            byte[]? expected = null;
            string? bad = null;
            try
            {
                expected = ParseHex(Expect);
            }
            catch (FormatException)
            {
                bad = $"{which} Expect '{Expect}' isn't hex bytes.";
            }

            if (bad is not null)
            {
                yield return bad;
            }

            if (expected is not null && BitWidth == 0 && EffectiveSize is { } size && expected.Length != size)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"{which} Expect has {expected.Length} byte(s) but the field is {size}.");
            }
        }
    }
}

/// <summary>A field's place in a frame: where it starts and how many bits it takes (0 for the open-ended last field of a variable frame).</summary>
internal sealed record FrameSlot(FrameField Field, int StartBit, int BitLength)
{
    public int StartByte => StartBit / 8;
}

/// <summary>The resolved layout of a <see cref="FrameSchema"/>: the fixed bytes up to any variable tail, plus the checksum trailer.</summary>
internal sealed record FrameLayout(IReadOnlyList<FrameSlot> Slots, int FixedBytes, int TailIndex, int ChecksumBytes)
{
    /// <summary>The shortest frame: everything fixed, an empty tail, and the checksum.</summary>
    public int MinLength => FixedBytes + ChecksumBytes;
}

/// <summary>A checksum the frame ends with. It covers the bytes from <see cref="Start"/> (0 is the first sync byte) up to the checksum itself.</summary>
public sealed class FrameChecksum
{
    private static readonly string[] _kinds = ["sum8", "xor8", "crc8", "crc16-modbus", "crc16-ccitt"];

    /// <summary><c>sum8</c> (low byte of the sum), <c>xor8</c>, <c>crc8</c> (poly 0x07), <c>crc16-modbus</c> (reflected 0xA001, init 0xFFFF; low byte first on the wire) or <c>crc16-ccitt</c> (CCITT-FALSE, poly 0x1021, init 0xFFFF; high byte first).</summary>
    public string Kind { get; set; } = "sum8";

    /// <summary>Bytes of the frame to skip before the covered data starts: 0 covers the sync bytes too, the sync's length starts just after them.</summary>
    public int Start { get; set; }

    /// <summary><c>le</c> or <c>be</c> for a 16-bit checksum's byte order on the wire; the kind's usual order when unset.</summary>
    public string? Endian { get; set; }

    /// <summary>The checksum's size in bytes, or null for an unknown kind.</summary>
    public static int? SizeOf(string kind) => kind switch
    {
        "sum8" or "xor8" or "crc8" => 1,
        "crc16-modbus" or "crc16-ccitt" => 2,
        _ => null,
    };

    internal IEnumerable<string> Validate()
    {
        if (SizeOf(Kind) is null)
        {
            yield return $"Frame checksum kind '{Kind}' must be one of {string.Join(", ", _kinds)}.";
        }

        if (Start < 0)
        {
            yield return "Frame checksum Start can't be negative.";
        }

        if (Endian is not null && !string.Equals(Endian, "le", StringComparison.OrdinalIgnoreCase) && !string.Equals(Endian, "be", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"Frame checksum endian '{Endian}' must be 'le' or 'be'.";
        }
    }

    /// <summary>True when the frame's trailing checksum matches the covered bytes. <paramref name="frame"/> is the whole frame.</summary>
    internal bool Matches(ReadOnlySpan<byte> frame)
    {
        var size = SizeOf(Kind) ?? 0;
        if (size == 0 || frame.Length < size + Start)
        {
            return false;
        }

        var covered = frame[Start..^size];
        var trailer = frame[^size..];
        var computed = Compute(Kind, covered);
        if (size == 1)
        {
            return trailer[0] == (byte)computed;
        }

        var big = Endian is { } e ? string.Equals(e, "be", StringComparison.OrdinalIgnoreCase) : Kind == "crc16-ccitt";
        var onWire = big ? (ushort)((trailer[0] << 8) | trailer[1]) : (ushort)((trailer[1] << 8) | trailer[0]);
        return onWire == (ushort)computed;
    }

    internal static uint Compute(string kind, ReadOnlySpan<byte> data)
    {
        switch (kind)
        {
            case "sum8":
                var sum = 0;
                foreach (var b in data)
                {
                    sum += b;
                }

                return (uint)(sum & 0xFF);
            case "xor8":
                var x = 0;
                foreach (var b in data)
                {
                    x ^= b;
                }

                return (uint)x;
            case "crc8":
                var crc8 = 0;
                foreach (var b in data)
                {
                    crc8 ^= b;
                    for (var i = 0; i < 8; i++)
                    {
                        crc8 = (crc8 & 0x80) != 0 ? ((crc8 << 1) ^ 0x07) & 0xFF : (crc8 << 1) & 0xFF;
                    }
                }

                return (uint)crc8;
            case "crc16-modbus":
                var modbus = 0xFFFF;
                foreach (var b in data)
                {
                    modbus ^= b;
                    for (var i = 0; i < 8; i++)
                    {
                        modbus = (modbus & 1) != 0 ? (modbus >> 1) ^ 0xA001 : modbus >> 1;
                    }
                }

                return (uint)modbus;
            case "crc16-ccitt":
                var ccitt = 0xFFFF;
                foreach (var b in data)
                {
                    ccitt ^= b << 8;
                    for (var i = 0; i < 8; i++)
                    {
                        ccitt = (ccitt & 0x8000) != 0 ? ((ccitt << 1) ^ 0x1021) & 0xFFFF : (ccitt << 1) & 0xFFFF;
                    }
                }

                return (uint)ccitt;
            default:
                return 0;
        }
    }
}
