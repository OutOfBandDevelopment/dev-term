using System.Text;
using DevTerm.Logging;
using DevTerm.Test.Utilities;

namespace DevTerm.DeviceManifests.Tests;

[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class RecordedSamplesTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static DeviceManifest Manifest() => new()
    {
        Name = "Meter",
        Inbound = new InboundProtocol { Patterns = [new ResponsePattern { Name = "reading", Match = @"^MEAS (?<volts>-?[\d.]+) V (?<state>\w+)$", Example = "MEAS 1.0 V OK" }] },
    };

    private static SessionLog Log(params (SessionLogRecordKind Kind, string Text)[] records) =>
        new(new SessionLogHeader { Created = _t0 }, records.Select((r, i) => new SessionLogRecord
        {
            Kind = r.Kind,
            Sequence = i + 1,
            Timestamp = _t0.AddSeconds(i),
            Data = Encoding.ASCII.GetBytes(r.Text),
        }));

    [TestMethod]
    public void FromSessionLog_KeepsEveryValueTheReceivedLinesPublish_InOrder_IgnoringTx()
    {
        var log = Log(
            (SessionLogRecordKind.Tx, "MEAS?\n"),
            (SessionLogRecordKind.Rx, "MEAS 12.5 V OK\nMEAS 12.7 V "),
            (SessionLogRecordKind.Rx, "WARN\n"),
            (SessionLogRecordKind.Rx, "noise line\n"));

        var samples = RecordedSamples.FromSessionLog(Manifest(), log);

        Assert.AreEqual(2, samples.Count("volts"));
        Assert.AreEqual(12.5, samples.Number("volts", 0));
        Assert.AreEqual(12.7, samples.Number("volts", 1));
        Assert.AreEqual(12.5, samples.Number("volts", 2), "wraps around");
        Assert.AreEqual("WARN", samples.Text("state", 1));
        Assert.IsFalse(samples.IsEmpty);
    }

    [TestMethod]
    public void ALogWithNothingTheManifestRecognises_IsEmpty()
    {
        Assert.IsTrue(RecordedSamples.FromSessionLog(Manifest(), Log((SessionLogRecordKind.Rx, "hello\n"))).IsEmpty);
    }

    [TestMethod]
    public void Generator_PrefersARecording_OverExampleAndGeneratedValues()
    {
        var samples = RecordedSamples.FromSessionLog(Manifest(), Log((SessionLogRecordKind.Rx, "MEAS 12.5 V OK\nMEAS 12.7 V WARN\n")));
        var paths = ValuePathCatalog.Enumerate(Manifest());
        var volts = paths.Single(p => p.Path == "volts");
        var state = paths.Single(p => p.Path == "state");

        Assert.AreEqual(12.5, SampleDataGenerator.Value(volts, 1, 0, samples));
        Assert.AreEqual(12.7, SampleDataGenerator.Value(volts, 1, 1, samples));
        Assert.AreEqual("WARN", SampleDataGenerator.TextValues([state], 1, 1, samples)["state"]);
        Assert.AreEqual(1.0, SampleDataGenerator.Value(volts, 1, 0), "no recording: the declared example");
    }

    [TestMethod]
    public void APathTheRecordingLacks_StillGenerates()
    {
        var samples = RecordedSamples.FromSessionLog(Manifest(), Log((SessionLogRecordKind.Rx, "MEAS 12.5 V OK\n")));
        var other = new ValuePath("vset", ValuePathType.Number, ValuePathSource.Control, "vset", Minimum: 0, Maximum: 30);

        Assert.AreEqual(SampleDataGenerator.Value(other, 1, 4), SampleDataGenerator.Value(other, 1, 4, samples));
    }

    [TestMethod]
    public void ExpressionPicker_EvaluatesAgainstTheRecording_AndRestartsTheWalkWhenItChanges()
    {
        var samples = RecordedSamples.FromSessionLog(Manifest(), Log((SessionLogRecordKind.Rx, "MEAS 12.5 V OK\nMEAS 12.7 V OK\n")));
        var picker = new Editing.ExpressionPickerViewModel(ValuePathCatalog.Enumerate(Manifest()), "{volts} * 2");

        picker.NextSample();
        picker.Recording = samples;
        Assert.AreEqual(25.0, picker.Result);
        picker.NextSample();
        Assert.AreEqual(25.4, picker.Result!.Value, 1e-9);
    }
}
