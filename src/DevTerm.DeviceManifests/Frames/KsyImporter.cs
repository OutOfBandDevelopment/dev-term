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
/// <see cref="FrameSchema.Sync"/>); and <c>doc</c> as a label; user types from <c>types</c> (flattened to <c>parent.child</c> names) and <c>repeat: expr</c> with a
/// literal count (<c>name[0]</c>, <c>name[1]</c>, ...). Anything dynamic (<c>repeat-until</c>, <c>repeat: eos</c>, a computed
/// count, <c>if</c>, <c>switch-on</c>, <c>size-eos</c>) ends the frame there with a warning, since later offsets are no longer known.
/// See docs/design/features/ksy-importer.md.
/// </summary>
public static class KsyImporter
{
    private static readonly string[] _unsupported = ["repeat-until", "if", "size-eos", "terminator", "process", "pos", "io"];

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

        var types = map.TryGetValue("types", out var typesValue) && typesValue is Dictionary<object, object> typeMap ? typeMap : [];
        var walk = new Walk(schema, warnings, types);
        walk.Sequence(seq, string.Empty, 0);
        if (walk.Sync.Count > 0)
        {
            schema.Sync = Convert.ToHexString([.. walk.Sync]);
        }

        if (schema.Fields.Count == 0)
        {
            warnings.Add("No attribute could be imported.");
            return new KsyImportResult(null, warnings);
        }

        return new KsyImportResult(schema, warnings);
    }

    /// <summary>One import in progress: appends fields to the schema, naming nested user types <c>parent.child</c> and repeated attributes <c>name[0]</c>.</summary>
    private sealed class Walk(FrameSchema schema, List<string> warnings, Dictionary<object, object> types)
    {
        private const int _maxRepeat = 256;
        private const int _maxDepth = 8;
        private bool _leadingMagic = true;

        public List<byte> Sync { get; } = [];

        /// <summary>Adds each attribute in turn; false once one stops the frame (everything after it has an unknown offset).</summary>
        public bool Sequence(List<object> seq, string prefix, int depth)
        {
            foreach (var item in seq)
            {
                if (item is not Dictionary<object, object> attr)
                {
                    warnings.Add("A seq entry is not a mapping; the frame stops there.");
                    return false;
                }

                var id = Text(attr, "id") ?? string.Empty;
                if (UnsupportedKey(attr) is { } key)
                {
                    warnings.Add($"Attribute '{prefix}{id}' uses '{key}', which the frame model cannot express; the frame stops before it.");
                    return false;
                }

                if (!Repeated(attr, prefix + id, out var count))
                {
                    return false;
                }

                for (var i = 0; i < count; i++)
                {
                    var name = attr.ContainsKey("repeat") ? $"{prefix}{id}[{i}]" : prefix + id;
                    if (!Attribute(attr, name, depth))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool Repeated(Dictionary<object, object> attr, string name, out int count)
        {
            count = 1;
            if (!attr.ContainsKey("repeat"))
            {
                return true;
            }

            if (Text(attr, "repeat") != "expr" || !int.TryParse(Text(attr, "repeat-expr"), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count is < 1 or > _maxRepeat)
            {
                warnings.Add($"Attribute '{name}' repeats a count the frame model cannot fix (only 'repeat: expr' with a literal 1 to {_maxRepeat}); the frame stops before it.");
                return false;
            }

            return true;
        }

        private bool Attribute(Dictionary<object, object> attr, string name, int depth)
        {
            var field = new FrameField { Name = name, Label = Text(attr, "doc") };
            var contents = Contents(attr);
            var type = Text(attr, "type");
            int? size = int.TryParse(Text(attr, "size"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

            if (contents is not null)
            {
                field.Type = "skip";
                field.Size = contents.Length;
                field.Expect = Convert.ToHexString(contents);
                if (_leadingMagic)
                {
                    Sync.AddRange(contents);
                }
            }
            else
            {
                if (type is not null && size is null && types.TryGetValue(type, out var nested))
                {
                    return Nested(nested, name, type, depth);
                }

                _leadingMagic = false;
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
                    warnings.Add($"Attribute '{name}' has type '{type ?? "(none)"}' without a fixed size the frame model can use; the frame stops before it.");
                    return false;
                }
            }

            schema.Fields.Add(field);
            return true;
        }

        private bool Nested(object definition, string name, string typeName, int depth)
        {
            if (depth >= _maxDepth)
            {
                warnings.Add($"Attribute '{name}' nests user types more than {_maxDepth} deep; the frame stops before it.");
                return false;
            }

            if (definition is not Dictionary<object, object> map || !map.TryGetValue("seq", out var seq) || seq is not List<object> list)
            {
                warnings.Add($"User type '{typeName}' (attribute '{name}') has no 'seq' list; the frame stops before it.");
                return false;
            }

            return Sequence(list, name + ".", depth + 1);
        }
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
