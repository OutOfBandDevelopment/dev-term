using System.Globalization;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace DevTerm.DeviceManifests;

/// <summary>What <see cref="KsyImporter.Import"/> made: the frame, and what in the <c>.ksy</c> couldn't be carried over.</summary>
public sealed record KsyImportResult(FrameSchema? Schema, IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a Kaitai Struct (<c>.ksy</c>) description of a fixed-layout record into a <see cref="FrameSchema"/> (our own
/// reading of the format, no Kaitai runtime). Handled: <c>meta.endian</c>; <c>seq</c> attributes of <c>u1..u8</c>,
/// <c>s1..s8</c>, <c>f4</c>, <c>f8</c> (with an <c>le</c>/<c>be</c> suffix), <c>str</c> and untyped <c>size</c> byte runs;
/// <c>contents</c> (a magic number, which becomes an <see cref="FrameField.Expect"/> and, when leading, the frame's
/// <see cref="FrameSchema.Sync"/>); and <c>doc</c> as a label. Anything dynamic (<c>repeat</c>, <c>if</c>, <c>switch-on</c>,
/// user types, <c>size-eos</c>) ends the frame there with a warning, since later offsets are no longer known.
/// See docs/design/proposals/ksy-importer.md.
/// </summary>
public static class KsyImporter
{
    private static readonly string[] _unsupported = ["repeat", "repeat-expr", "repeat-until", "if", "size-eos", "terminator", "process", "pos", "io"];

    public static KsyImportResult Import(string ksy)
    {
        ArgumentNullException.ThrowIfNull(ksy);

        object? root;
        try
        {
            root = new DeserializerBuilder().Build().Deserialize<object>(ksy);
        }
        catch (YamlException ex)
        {
            return new KsyImportResult(null, [$"The .ksy is not valid YAML: {ex.Message}"]);
        }

        if (root is not Dictionary<object, object> map || !map.TryGetValue("seq", out var seqValue) || seqValue is not List<object> seq)
        {
            return new KsyImportResult(null, ["The .ksy has no 'seq' list of attributes."]);
        }

        var warnings = new List<string>();
        var schema = new FrameSchema();
        if (map.TryGetValue("meta", out var meta) && meta is Dictionary<object, object> metaMap
            && metaMap.TryGetValue("endian", out var endian) && endian is string e)
        {
            if (e is "le" or "be")
            {
                schema.Endian = e;
            }
            else
            {
                warnings.Add($"meta.endian '{e}' is not le or be; using le.");
            }
        }

        var leadingMagic = true;
        var sync = new List<byte>();
        foreach (var item in seq)
        {
            if (item is not Dictionary<object, object> attr)
            {
                warnings.Add("A seq entry is not a mapping; the frame stops there.");
                break;
            }

            var id = Text(attr, "id") ?? string.Empty;
            if (UnsupportedKey(attr) is { } key)
            {
                warnings.Add($"Attribute '{id}' uses '{key}', which the frame model cannot express; the frame stops before it.");
                break;
            }

            var field = new FrameField { Name = id, Label = Text(attr, "doc") };
            var contents = Contents(attr);
            var type = Text(attr, "type");
            int? size = int.TryParse(Text(attr, "size"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

            if (contents is not null)
            {
                field.Type = "skip";
                field.Size = contents.Length;
                field.Expect = Convert.ToHexString(contents);
                if (leadingMagic)
                {
                    sync.AddRange(contents);
                }
            }
            else
            {
                leadingMagic = false;
                if (type is not null && TryNumber(type, out var baseType, out var fieldEndian))
                {
                    field.Type = baseType;
                    field.Endian = fieldEndian;
                }
                else if (type is "str" && size is not null)
                {
                    field.Type = "str";
                    field.Size = size;
                }
                else if (type is null && size is not null)
                {
                    field.Type = "bytes";
                    field.Size = size;
                }
                else
                {
                    warnings.Add($"Attribute '{id}' has type '{type ?? "(none)"}' without a fixed size the frame model can use; the frame stops before it.");
                    break;
                }
            }

            schema.Fields.Add(field);
        }

        if (sync.Count > 0)
        {
            schema.Sync = Convert.ToHexString([.. sync]);
        }

        if (schema.Fields.Count == 0)
        {
            warnings.Add("No attribute could be imported.");
            return new KsyImportResult(null, warnings);
        }

        return new KsyImportResult(schema, warnings);
    }

    private static string? UnsupportedKey(Dictionary<object, object> attr)
    {
        foreach (var key in _unsupported)
        {
            if (attr.ContainsKey(key))
            {
                return key;
            }
        }

        return attr.TryGetValue("type", out var type) && type is Dictionary<object, object> ? "type: switch-on" : null;
    }

    private static string? Text(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is string s ? s.Trim() : null;

    private static byte[]? Contents(Dictionary<object, object> attr)
    {
        if (!attr.TryGetValue("contents", out var value))
        {
            return null;
        }

        if (value is string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        if (value is not List<object> items)
        {
            return null;
        }

        var bytes = new List<byte>();
        foreach (var item in items)
        {
            if (item is not string s)
            {
                return null;
            }

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && byte.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                bytes.Add(hex);
            }
            else if (byte.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var dec))
            {
                bytes.Add(dec);
            }
            else
            {
                bytes.AddRange(Encoding.ASCII.GetBytes(s));
            }
        }

        return [.. bytes];
    }

    private static bool TryNumber(string type, out string baseType, out string? endian)
    {
        endian = null;
        baseType = type;
        if (type.Length > 2 && (type.EndsWith("le", StringComparison.Ordinal) || type.EndsWith("be", StringComparison.Ordinal)))
        {
            endian = type[^2..];
            baseType = type[..^2];
        }

        return baseType is "u1" or "s1" or "u2" or "s2" or "u4" or "s4" or "u8" or "s8" or "f4" or "f8";
    }
}
