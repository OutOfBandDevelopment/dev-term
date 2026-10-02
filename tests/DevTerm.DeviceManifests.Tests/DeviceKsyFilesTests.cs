using DevTerm.Test.Utilities;

namespace DevTerm.DeviceManifests.Tests;

/// <summary>The <c>.ksy</c> files under <c>docs/devices</c> describe real device frames; each must import and decode a known frame.</summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class DeviceKsyFilesTests
{
    [TestMethod]
    public void De5000_ImportsAndDecodesAPacket()
    {
        var (schema, values) = Decode("de5000/de5000.ksy", [0x00, 0x0D, 0x00, 0x40, 0x00, 0x02, 0x04, 0xD2, 0x4A, 0x00, 0x01, 0x00, 0x64, 0x00, 0x00, 0x0D, 0x0A]);

        Assert.AreEqual(17, schema.Length);
        Assert.AreEqual("1234", values["primary_raw"]);
        Assert.AreEqual("74", values["primary_unit"]);
        Assert.AreEqual("100", values["secondary_raw"]);
    }

    [TestMethod]
    public void RadexOne_ImportsAndDecodesAReadDataReply()
    {
        var packet = new byte[]
        {
            0x7A, 0xFF, 0x20, 0x80, 0x16, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x08, 0x00, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x12, 0x00, 0x00, 0x00, 0x12, 0x00, 0x00, 0x00, 0x15, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        var (schema, values) = Decode("radexone/radexone-read-data-reply.ksy", packet);

        Assert.AreEqual(packet.Length, schema.Length);
        Assert.AreEqual("18", values["extension.ambient"]);
        Assert.AreEqual("18", values["extension.accumulated"]);
        Assert.AreEqual("21", values["extension.cpm"]);
    }

    /// <summary>Bytes captured from a real Radex One on COM8 (2026-10-02, scripts/radexone_probe.py): the request we send and the reply it gave.</summary>
    [TestMethod]
    public void RadexOne_RealCapturedReadDataExchange_DecodesWithTheShippedLayouts()
    {
        var (request, requestValues) = Decode("radexone/radexone-query-request.ksy", Convert.FromHexString("7BFF2000060001000000" + "5D00" + "00080C00" + "F3F7"));
        Assert.AreEqual(18, request.Length);
        Assert.AreEqual("2048", requestValues["extension.command_code"]);
        Assert.AreEqual("12", requestValues["extension.word"]);

        var reply = Convert.FromHexString("7AFF2080160001000000" + "4D80" + "0008" + "0000" + "0C000000" + "09000000" + "57030000" + "0D000000" + "86F4");
        var (schema, values) = Decode("radexone/radexone-read-data-reply.ksy", reply);
        Assert.AreEqual(reply.Length, schema.Length);
        Assert.AreEqual("9", values["extension.ambient"]);
        Assert.AreEqual("855", values["extension.accumulated"]);
        Assert.AreEqual("13", values["extension.cpm"]);
    }

    [TestMethod]
    public void K8055_ImportsAndDecodesAnInputReport()
    {
        var (schema, values) = Decode("k8055/k8055-input-report.ksy", [0x00, 0x05, 0x03, 0x40, 0x80, 0x34, 0x12, 0x01, 0x00]);

        Assert.AreEqual(9, schema.Length);
        Assert.AreEqual("5", values["digital_in"]);
        Assert.AreEqual("64", values["analog_in_1"]);
        Assert.AreEqual("4660", values["counter_1"]);
        Assert.AreEqual("1", values["counter_2"]);
    }

    [TestMethod]
    public void ZoomH4n_DocumentsBitFields_TheImporterCannotYetReadAsAFrame()
    {
        var result = KsyImporter.Import(File.ReadAllText(Path.Combine(DevicesDirectory(), "zoom-h4n", "zoom-h4n-status.ksy")));

        Assert.IsNotEmpty(result.Warnings);
    }

    [TestMethod]
    public void RadexOne_ImportsAndDecodesTheRequests()
    {
        var (query, queryValues) = Decode("radexone/radexone-query-request.ksy", [0x7B, 0xFF, 0x20, 0x00, 0x06, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x0C, 0x00, 0x00, 0x00]);
        Assert.AreEqual(18, query.Length);
        Assert.AreEqual("2048", queryValues["extension.command_code"]);

        var (write, writeValues) = Decode("radexone/radexone-write-settings-request.ksy", [0x7B, 0xFF, 0x20, 0x00, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x08, 0x0E, 0x00, 0x05, 0x00, 0x00, 0x00, 0x01, 0x64, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        Assert.AreEqual(28, write.Length);
        Assert.AreEqual("1", writeValues["extension.alarm_mode"]);
        Assert.AreEqual("100", writeValues["extension.threshold"]);
    }

    [TestMethod]
    public void K8055_ImportsAndDecodesAnOutputReport()
    {
        var (schema, values) = Decode("k8055/k8055-output-report.ksy", [0x00, 0x05, 0x0F, 0x80, 0x40, 0x00, 0x00, 0x00, 0x00]);

        Assert.AreEqual(9, schema.Length);
        Assert.AreEqual("5", values["command"]);
        Assert.AreEqual("15", values["digital_out"]);
        Assert.AreEqual("128", values["analog_out_1"]);
    }

    [TestMethod]
    public void Busylight_ImportsAndDecodesACommand()
    {
        var (schema, values) = Decode("busylight/busylight-command.ksy", [0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x01, 0x00, 0x00]);

        Assert.AreEqual(9, schema.Length);
        Assert.AreEqual("255", values["red"]);
        Assert.AreEqual("1", values["on_time"]);
    }

    [TestMethod]
    public void ZoomH4n_ImportsAndDecodesAPress()
    {
        var (schema, values) = Decode("zoom-h4n/zoom-h4n-command.ksy", [0x80, 0x08]);

        Assert.AreEqual(2, schema.Length);
        Assert.AreEqual("8", values["button_bits"]);
    }

    private static (FrameSchema Schema, Dictionary<string, string> Values) Decode(string relativePath, byte[] bytes)
    {
        var result = KsyImporter.Import(File.ReadAllText(Path.Combine(DevicesDirectory(), relativePath)));
        Assert.IsNotNull(result.Schema, string.Join("; ", result.Warnings));
        Assert.AreEqual(0, result.Warnings.Count, string.Join("; ", result.Warnings));
        Assert.AreEqual(0, result.Schema.Validate().Count);
        var values = new Dictionary<string, string>();
        Assert.IsTrue(new FrameDecoder(result.Schema).TryDecode(bytes, values));
        return (result.Schema, values);
    }

    private static string DevicesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevTerm.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Could not find DevTerm.slnx."), "docs", "devices");
    }
}
