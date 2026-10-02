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

    /// <summary>The whole frame's length in bytes, or null when a field's size is unknown (<see cref="Validate"/> reports why).</summary>
    [JsonIgnore]
    public int? Length
    {
        get
        {
            var total = 0;
            foreach (var item in Fields)
            {
                if (item.EffectiveSize is not { } size)
                {
                    return null;
                }

                total += size;
            }

            return total;
        }
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
        foreach (var field in Fields)
        {
            errors.AddRange(field.Validate());
            if (field.Publishes && !string.IsNullOrWhiteSpace(field.Name) && !names.Add(field.Name))
            {
                errors.Add($"Frame field '{field.Name}' appears more than once.");
            }
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

    /// <summary><c>u1 u2 u4 u8 s1 s2 s4 s8 f4 f8</c> (numbers), <c>str</c> (ASCII text, trailing NULs and spaces dropped), <c>bytes</c> (published as hex) or <c>skip</c> (not published).</summary>
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

    [JsonIgnore]
    public bool IsNumber => _fixedSizes.ContainsKey(Type);

    [JsonIgnore]
    public int? EffectiveSize => _fixedSizes.TryGetValue(Type, out var size) ? size : Size is > 0 ? Size : null;

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

    internal IEnumerable<string> Validate()
    {
        var which = string.IsNullOrWhiteSpace(Name) ? $"A '{Type}' frame field" : $"Frame field '{Name}'";
        if (Publishes && string.IsNullOrWhiteSpace(Name))
        {
            yield return $"{which} needs a name.";
        }

        if (!IsNumber && Type is not ("str" or "bytes" or "skip"))
        {
            yield return $"{which} has an unknown type '{Type}' (u1 u2 u4 u8 s1 s2 s4 s8 f4 f8 str bytes skip).";
        }
        else if (!IsNumber && Size is not > 0)
        {
            yield return $"{which} is '{Type}' and needs a Size.";
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

            if (expected is not null && EffectiveSize is { } size && expected.Length != size)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"{which} Expect has {expected.Length} byte(s) but the field is {size}.");
            }
        }
    }
}
