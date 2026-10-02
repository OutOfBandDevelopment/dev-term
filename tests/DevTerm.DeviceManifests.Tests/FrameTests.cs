using System.Buffers;
using DevTerm.Test.Utilities;
using DevTerm.UiDefinitions;

namespace DevTerm.DeviceManifests.Tests;

/// <summary>Binary frames: <see cref="FrameSchema"/> / <see cref="FrameDecoder"/>, <see cref="ManifestFramePresenter"/> and <see cref="KsyImporter"/>.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class FrameTests
{
    private const string _ksy = """
        meta:
          id: sensor
          endian: be
        seq:
          - id: magic
            contents: [0xA5, 0x5A]
          - id: volts
            type: u2
            doc: Supply voltage
          - id: temp
            type: s2le
          - id: name
            type: str
            size: 4
          - id: blob
            size: 2
        """;

    private static FrameSchema Schema() => new()
    {
        Sync = "A5 5A",
        Endian = "be",
        Fields =
        [
            new FrameField { Type = "skip", Size = 2, Expect = "A55A" },
            new FrameField { Name = "volts", Type = "u2", Scale = 0.1 },
            new FrameField { Name = "temp", Type = "s2", Endian = "le" },
        ],
    };

    [TestMethod]
    public void Decoder_ReadsScaledAndMixedEndianFields()
    {
        var decoder = new FrameDecoder(Schema());
        var values = new Dictionary<string, string>();

        Assert.AreEqual(6, decoder.Length);
        Assert.IsTrue(decoder.TryDecode([0xA5, 0x5A, 0x00, 0x7B, 0xFF, 0xFF], values));
        Assert.AreEqual("12.3", values["volts"]);
        Assert.AreEqual("-1", values["temp"]);
        Assert.IsFalse(values.ContainsKey("A55A"));
    }

    [TestMethod]
    public void Decoder_RejectsWrongMagic_AndShortFrames()
    {
        var decoder = new FrameDecoder(Schema());
        var values = new Dictionary<string, string>();

        Assert.IsFalse(decoder.TryDecode([0xA5, 0x00, 0, 0, 0, 0], values));
        Assert.IsFalse(decoder.TryDecode([0xA5, 0x5A, 0], values));
        Assert.AreEqual(0, values.Count);
    }

    [TestMethod]
    public void Schema_ReportsProblems()
    {
        var bad = new FrameSchema
        {
            Sync = "ZZ",
            Fields = [new FrameField { Name = "a", Type = "str" }, new FrameField { Name = "a", Type = "u1" }, new FrameField { Name = "b", Type = "nope" }],
        };

        var errors = bad.Validate();
        Assert.IsTrue(errors.Any(e => e.Contains("sync", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(e => e.Contains("needs a Size", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(e => e.Contains("more than once", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(e => e.Contains("unknown type", StringComparison.Ordinal)));
        Assert.ThrowsExactly<ArgumentException>(() => new FrameDecoder(bad));
    }

    [TestMethod]
    public void Presenter_ResyncsPastGarbage_AndHandlesSplitReads()
    {
        var presenter = new ManifestFramePresenter(Schema());
        var seen = new List<IReadOnlyDictionary<string, string>>();
        presenter.ValuesChanged += (_, v) => seen.Add(v);

        Assert.AreEqual(0, presenter.Render(new ReadOnlySequence<byte>([0x01, 0x02, 0xA5, 0x5A, 0x00])).Count);
        Assert.AreEqual(0, seen.Count);

        presenter.Render(new ReadOnlySequence<byte>([0x0A, 0x01, 0x00, 0xA5, 0x5A, 0x00, 0x14, 0x02, 0x00]));

        Assert.AreEqual(2, seen.Count);
        Assert.AreEqual("1", seen[0]["volts"]);
        Assert.AreEqual("1", seen[0]["temp"]);
        Assert.AreEqual("2", seen[1]["volts"]);
        Assert.AreEqual("2", seen[1]["temp"]);
    }

    [TestMethod]
    public void KsyImporter_BuildsAFrame()
    {
        var result = KsyImporter.Import(_ksy);

        Assert.IsNotNull(result.Schema);
        Assert.AreEqual(0, result.Warnings.Count);
        Assert.AreEqual("A55A", result.Schema.Sync);
        Assert.AreEqual("be", result.Schema.Endian);
        Assert.AreEqual(12, result.Schema.Length);
        CollectionAssert.AreEqual(new[] { "magic", "volts", "temp", "name", "blob" }, result.Schema.Fields.Select(f => f.Name).ToArray());
        Assert.AreEqual("skip", result.Schema.Fields[0].Type);
        Assert.AreEqual("Supply voltage", result.Schema.Fields[1].Label);
        Assert.AreEqual("le", result.Schema.Fields[2].Endian);
        Assert.AreEqual("bytes", result.Schema.Fields[4].Type);
        Assert.AreEqual(0, result.Schema.Validate().Count);
    }

    [TestMethod]
    public void KsyImporter_StopsAtAnythingDynamic_WithAWarning()
    {
        var result = KsyImporter.Import("""
            seq:
              - id: count
                type: u1
              - id: items
                type: u1
                repeat: expr
                repeat-expr: count
              - id: after
                type: u1
            """);

        Assert.IsNotNull(result.Schema);
        Assert.AreEqual(1, result.Schema.Fields.Count);
        StringAssert.Contains(result.Warnings.Single(), "repeat");
    }

    private const string _nestedKsy = """
        meta:
          endian: le
        seq:
          - id: header
            type: header
          - id: samples
            type: u2
            repeat: expr
            repeat-expr: 3
          - id: points
            type: point
            repeat: expr
            repeat-expr: 2
        types:
          header:
            seq:
              - id: length
                type: u1
              - id: kind
                type: u1
          point:
            seq:
              - id: x
                type: s1
              - id: y
                type: s1
        """;

    [TestMethod]
    public void KsyImporter_FlattensNestedTypesAndFixedRepeats_IntoDottedAndIndexedNames()
    {
        var result = KsyImporter.Import(_nestedKsy);

        Assert.IsNotNull(result.Schema);
        Assert.AreEqual(0, result.Warnings.Count);
        CollectionAssert.AreEqual(
            new[] { "header.length", "header.kind", "samples[0]", "samples[1]", "samples[2]", "points[0].x", "points[0].y", "points[1].x", "points[1].y" },
            result.Schema.Fields.Select(f => f.Name).ToArray());
        Assert.AreEqual(12, result.Schema.Length);
        Assert.AreEqual(0, result.Schema.Validate().Count);
    }

    [TestMethod]
    public void KsyImporter_NestedFrame_DecodesIntoTheFlattenedValues()
    {
        var schema = KsyImporter.Import(_nestedKsy).Schema!;
        var decoder = new FrameDecoder(schema);
        var values = new Dictionary<string, string>();

        Assert.IsTrue(decoder.TryDecode([12, 7, 1, 0, 2, 0, 3, 0, 5, 0xFE, 6, 0xFD], values));

        Assert.AreEqual("12", values["header.length"]);
        Assert.AreEqual("2", values["samples[1]"]);
        Assert.AreEqual("-2", values["points[0].y"]);
        Assert.AreEqual("6", values["points[1].x"]);
    }

    [TestMethod]
    public void KsyImporter_IndexedAndDottedPaths_AreUsableInAnExpression()
    {
        var values = new Dictionary<string, double> { ["header.length"] = 12, ["samples[1]"] = 2 };

        Assert.AreEqual(14, Expression.Parse("{header.length} + {samples[1]}").Evaluate(values));
    }

    [TestMethod]
    public void KsyImporter_UserTypeThatRecurses_StopsWithAWarning()
    {
        var result = KsyImporter.Import("""
            seq:
              - id: a
                type: u1
              - id: loop
                type: node
            types:
              node:
                seq:
                  - id: inner
                    type: node
            """);

        Assert.IsNotNull(result.Schema);
        Assert.AreEqual(1, result.Schema.Fields.Count);
        StringAssert.Contains(result.Warnings.Single(), "deep");
    }

    [TestMethod]
    [DataRow("not: [valid")]
    [DataRow("meta:\n  id: x\n")]
    [DataRow("seq:\n  - id: a\n    type: foo\n")]
    public void KsyImporter_RefusesWhatItCannotUse_WithoutThrowing(string ksy)
    {
        var result = KsyImporter.Import(ksy);

        Assert.IsNull(result.Schema);
        Assert.IsTrue(result.Warnings.Count > 0);
    }

    private static DeviceManifest ManifestWithFrame() => new()
    {
        Name = "Framed",
        Inbound = new InboundProtocol { Frame = Schema() },
    };

    [TestMethod]
    public void Catalog_ListsPublishedFrameFields_NotSkippedOnes()
    {
        var paths = ValuePathCatalog.Enumerate(ManifestWithFrame());

        CollectionAssert.AreEqual(new[] { "volts", "temp" }, paths.Select(p => p.Path).ToArray());
        Assert.IsTrue(paths.All(p => p.Source == ValuePathSource.Frame && p.Type == ValuePathType.Number));
    }

    [TestMethod]
    public void Validator_ReportsAnInvalidFrame_AndAcceptsAGoodOne()
    {
        Assert.IsTrue(DeviceManifestValidator.Validate(ManifestWithFrame()).IsValid);

        var bad = ManifestWithFrame();
        bad.Inbound!.Frame!.Fields.Add(new FrameField { Name = "x", Type = "bogus" });

        Assert.IsFalse(DeviceManifestValidator.Validate(bad).IsValid);
    }

    [TestMethod]
    public void Frame_SurvivesJsonAndXmlRoundTrips()
    {
        var manifest = ManifestWithFrame();

        foreach (var back in new[]
        {
            DeviceManifestSerializer.FromJson(DeviceManifestSerializer.ToJson(manifest)),
            DeviceManifestSerializer.FromXml(DeviceManifestSerializer.ToXml(manifest)),
        })
        {
            var frame = back.Inbound!.Frame!;
            Assert.AreEqual("A55A", frame.Sync!.Replace(" ", string.Empty, StringComparison.Ordinal));
            Assert.AreEqual(3, frame.Fields.Count);
            Assert.AreEqual(0.1, frame.Fields[1].Scale);
            Assert.AreEqual("le", frame.Fields[2].Endian);
        }
    }
}
