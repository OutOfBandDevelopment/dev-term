using System.Buffers;
using System.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.DeviceManifests.Tests;

/// <summary>Frame bit fields, variable-length (length-prefixed) frames and checksums.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class FrameAdvancedTests
{
    private static List<IReadOnlyDictionary<string, string>> Feed(ManifestFramePresenter presenter, params byte[][] reads)
    {
        var frames = new List<IReadOnlyDictionary<string, string>>();
        presenter.ValuesChanged += (_, values) => frames.Add(new Dictionary<string, string>(values));
        foreach (var read in reads)
        {
            presenter.Render(new ReadOnlySequence<byte>(read));
        }

        return frames;
    }

    private static FrameSchema Status() => new()
    {
        Fields =
        [
            new FrameField { Name = "recording", Type = "b1" },
            new FrameField { Name = "mode", Type = "b3" },
            new FrameField { Name = "level", Type = "b4" },
            new FrameField { Name = "wide", Type = "b12" },
            new FrameField { Name = "tail", Type = "b4" },
            new FrameField { Name = "count", Type = "u1" },
        ],
    };

    [TestMethod]
    public void BitFields_PackMsbFirst_AndByteFieldsAlignToTheNextByte()
    {
        var decoder = new FrameDecoder(Status());
        var values = new Dictionary<string, string>();

        // 1011 0010 | 1010 1010 1010 0110 | 0x2A: recording=1 mode=011 level=0010 wide=0xAAA tail=0110, then count on its own byte.
        Assert.AreEqual(4, decoder.Length);
        Assert.IsTrue(decoder.TryDecode([0xB2, 0xAA, 0xA6, 0x2A], values));
        Assert.AreEqual("1", values["recording"]);
        Assert.AreEqual("3", values["mode"]);
        Assert.AreEqual("2", values["level"]);
        Assert.AreEqual("2730", values["wide"]);
        Assert.AreEqual("6", values["tail"]);
        Assert.AreEqual("42", values["count"]);
    }

    [TestMethod]
    public void BitFields_AreNumbersInTheValueCatalog_AndRejectExpect()
    {
        Assert.IsTrue(Status().Fields[0].IsNumber);
        Assert.AreEqual(12, Status().Fields[3].BitWidth);

        var schema = Status();
        schema.Fields[0].Expect = "01";
        Assert.IsTrue(schema.Validate().Any(error => error.Contains("bit field", StringComparison.Ordinal)));
        Assert.AreEqual(0, new FrameField { Type = "b65" }.BitWidth);
        Assert.AreEqual(0, new FrameField { Type = "bytes" }.BitWidth);
    }

    private static FrameSchema LengthPrefixed() => new()
    {
        Sync = "A5",
        LengthField = "length",
        LengthAdjust = 2,
        Fields =
        [
            new FrameField { Type = "skip", Size = 1, Expect = "A5" },
            new FrameField { Name = "length", Type = "u1" },
            new FrameField { Name = "payload", Type = "str" },
        ],
    };

    [TestMethod]
    public void VariableFrame_TakesItsLengthFromAField_AcrossSeveralReads()
    {
        var presenter = new ManifestFramePresenter(LengthPrefixed());
        var hello = Encoding.ASCII.GetBytes("hello");
        var ok = Encoding.ASCII.GetBytes("ok");

        var frames = Feed(
            presenter,
            [0x99, 0xA5, 5, .. hello[..2]],
            [.. hello[2..], 0xA5, 2],
            [.. ok]);

        Assert.AreEqual(2, frames.Count);
        Assert.AreEqual("hello", frames[0]["payload"]);
        Assert.AreEqual("5", frames[0]["length"]);
        Assert.AreEqual("ok", frames[1]["payload"]);
    }

    [TestMethod]
    public void VariableFrame_ProbeWaitsForTheLengthThenTheBody()
    {
        var decoder = new FrameDecoder(LengthPrefixed());

        Assert.IsTrue(decoder.IsVariable);
        Assert.AreEqual(2, decoder.Length);
        Assert.AreEqual(FrameProbe.NeedMore, decoder.Probe([0xA5], out _));
        Assert.AreEqual(FrameProbe.NeedMore, decoder.Probe([0xA5, 3, 0x41], out _));
        Assert.AreEqual(FrameProbe.Ready, decoder.Probe([0xA5, 3, 0x41, 0x42, 0x43, 0xFF], out var total));
        Assert.AreEqual(5, total);
    }

    [TestMethod]
    public void VariableFrame_AnAbsurdLengthIsInvalid_SoTheDecoderResyncsInsteadOfWaiting()
    {
        var schema = LengthPrefixed();
        schema.Fields[1].Type = "u4";
        schema.Fields[1].Endian = "be";
        var decoder = new FrameDecoder(schema);

        Assert.AreEqual(FrameProbe.Invalid, decoder.Probe([0xA5, 0xFF, 0xFF, 0xFF, 0xFF], out _));
    }

    [TestMethod]
    public void Validate_ReportsABrokenLengthField()
    {
        var schema = LengthPrefixed();
        schema.LengthField = "nope";
        Assert.IsTrue(schema.Validate().Any(error => error.Contains("LengthField", StringComparison.Ordinal)));

        var noTail = Status();
        noTail.LengthField = "count";
        Assert.IsTrue(noTail.Validate().Count > 0);
    }

    private static FrameSchema Checked(string kind) => new()
    {
        Checksum = new FrameChecksum { Kind = kind },
        Fields = [new FrameField { Name = "data", Type = "bytes", Size = 9 }],
    };

    [TestMethod]
    public void Checksum_KnownVectorsMatch_AndACorruptedFrameIsDropped()
    {
        var data = Encoding.ASCII.GetBytes("123456789");
        (string Kind, byte[] Trailer)[] cases =
        [
            ("sum8", [0xDD]),
            ("xor8", [0x31]),
            ("crc8", [0xF4]),
            ("crc16-modbus", [0x37, 0x4B]),
            ("crc16-ccitt", [0x29, 0xB1]),
        ];

        foreach (var (kind, trailer) in cases)
        {
            var decoder = new FrameDecoder(Checked(kind));
            var values = new Dictionary<string, string>();
            byte[] frame = [.. data, .. trailer];

            Assert.IsTrue(decoder.TryDecode(frame, values), kind);
            Assert.AreEqual(Convert.ToHexString(data), values["data"], kind);

            frame[2] ^= 0x01;
            Assert.IsFalse(decoder.TryDecode(frame, new Dictionary<string, string>()), kind + " corrupted");
        }
    }

    [TestMethod]
    public void Checksum_StartSkipsTheSync_AndAFailedFrameResyncsToTheNextOne()
    {
        var schema = new FrameSchema
        {
            Sync = "AA",
            Checksum = new FrameChecksum { Kind = "xor8", Start = 1 },
            Fields =
            [
                new FrameField { Type = "skip", Size = 1, Expect = "AA" },
                new FrameField { Name = "a", Type = "u1" },
                new FrameField { Name = "b", Type = "u1" },
            ],
        };
        var presenter = new ManifestFramePresenter(schema);

        // First frame has a wrong checksum (0x07 ^ 0x08 = 0x0F, not 0x00); the second is good.
        var frames = Feed(presenter, [0xAA, 0x07, 0x08, 0x00, 0xAA, 0x01, 0x02, 0x03]);

        Assert.AreEqual(1, frames.Count);
        Assert.AreEqual("1", frames[0]["a"]);
        Assert.AreEqual("2", frames[0]["b"]);
    }

    [TestMethod]
    public void Checksum_AfterAVariablePayload_CoversTheWholeBody()
    {
        var schema = LengthPrefixed();
        schema.Checksum = new FrameChecksum { Kind = "sum8" };
        schema.LengthAdjust = 3;
        var presenter = new ManifestFramePresenter(schema);

        // A5 | 02 | 'o' 'k' | sum8 of the first four bytes.
        byte[] body = [0xA5, 0x02, (byte)'o', (byte)'k'];
        var sum = (byte)body.Sum(b => b);
        var frames = Feed(presenter, [.. body, sum], [.. body, (byte)(sum + 1)]);

        Assert.AreEqual(1, frames.Count);
        Assert.AreEqual("ok", frames[0]["payload"]);
    }

    [TestMethod]
    public void Importer_ReadsBitTypes_AndASizeThatPointsAtAnEarlierField()
    {
        var result = KsyImporter.Import("""
            meta:
              id: status
              endian: be
            seq:
              - id: magic
                contents: [0xA5]
              - id: flag
                type: b1
              - id: mode
                type: b7
              - id: len
                type: u1
              - id: body
                size: len
            """);

        Assert.IsNotNull(result.Schema);
        Assert.AreEqual(0, result.Warnings.Count, string.Join("; ", result.Warnings));
        Assert.AreEqual("b1", result.Schema.Fields[1].Type);
        Assert.AreEqual("b7", result.Schema.Fields[2].Type);
        Assert.AreEqual("len", result.Schema.LengthField);
        Assert.AreEqual(3, result.Schema.LengthAdjust);
        Assert.AreEqual(0, result.Schema.Validate().Count, string.Join("; ", result.Schema.Validate()));

        var frames = Feed(new ManifestFramePresenter(result.Schema), [0xA5, 0x85, 0x02, 0x01, 0x02]);
        Assert.AreEqual(1, frames.Count);
        Assert.AreEqual("1", frames[0]["flag"]);
        Assert.AreEqual("5", frames[0]["mode"]);
        Assert.AreEqual("0102", frames[0]["body"]);
    }

    [TestMethod]
    public void Importer_StopsAfterAVariableSizeAttribute_BecauseLaterOffsetsAreUnknown()
    {
        var result = KsyImporter.Import("""
            seq:
              - id: len
                type: u1
              - id: body
                size: len
              - id: crc
                type: u2
            """);

        Assert.IsNotNull(result.Schema);
        Assert.AreEqual(2, result.Schema.Fields.Count);
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("variable-size", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LengthFieldAndChecksum_SurviveJsonAndXmlRoundTrips()
    {
        var schema = LengthPrefixed();
        schema.Checksum = new FrameChecksum { Kind = "crc16-modbus", Start = 1, Endian = "be" };
        var manifest = new DeviceManifest { Name = "Round trip", Inbound = new InboundProtocol { Frame = schema } };

        foreach (var back in new[]
        {
            DeviceManifestSerializer.FromJson(DeviceManifestSerializer.ToJson(manifest)),
            DeviceManifestSerializer.FromXml(DeviceManifestSerializer.ToXml(manifest)),
        })
        {
            var frame = back.Inbound!.Frame!;
            Assert.AreEqual("length", frame.LengthField);
            Assert.AreEqual(2, frame.LengthAdjust);
            Assert.AreEqual("crc16-modbus", frame.Checksum!.Kind);
            Assert.AreEqual(1, frame.Checksum.Start);
            Assert.AreEqual("be", frame.Checksum.Endian);
        }
    }
}
