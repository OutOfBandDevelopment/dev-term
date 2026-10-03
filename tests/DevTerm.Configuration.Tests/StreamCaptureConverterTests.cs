using DevTerm.Core.StreamContent;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// Covers <see cref="StreamCaptureConverter"/>'s three conversion mechanisms (internal HP-GL-to-SVG,
/// external tool), placeholder-substitution safety, and each mechanism's failure paths.
/// See docs/design/features/stream-content-detection.md's "Raster/convert tool integration".
/// </summary>
[TestClass]
[TestCategory(TestCategories.Unit)]
[DoNotParallelize]
public sealed class StreamCaptureConverterTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Initialize() => _directory = Directory.CreateTempSubdirectory("devterm-convert-tests-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static StreamCaptureConverter Converter(StreamCaptureConverterOptions options) =>
        new(Options.Create(options));

    private StreamMonitorCapture SavedCapture(StreamContentKind kind, byte[] data, string? fileName = null)
    {
        var path = Path.Combine(_directory, fileName ?? ("capture." + kind.Extension));
        File.WriteAllBytes(path, data);
        var capture = new StreamCapture(kind, data, DateTimeOffset.Now, StreamCaptureEnd.Complete, WasDeclared: false);
        return new StreamMonitorCapture(capture, "unit-test-device", DateTimeOffset.Now, path, null);
    }

    private static StreamMonitorCapture UnsavedCapture(StreamContentKind kind, byte[] data)
    {
        var capture = new StreamCapture(kind, data, DateTimeOffset.Now, StreamCaptureEnd.Complete, WasDeclared: false);
        return new StreamMonitorCapture(capture, "unit-test-device", DateTimeOffset.Now, null, "disk full");
    }

    [TestMethod]
    public async Task ConvertAsync_ModeNone_FailsWithExplanation()
    {
        var converter = Converter(new StreamCaptureConverterOptions());
        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.OutputPath);
        StringAssert.Contains(result.Error, "No conversion mechanism");
    }

    [TestMethod]
    public async Task ConvertAsync_InternalHpglToSvg_WritesSvgNextToTheSavedFile()
    {
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.InternalHpglToSvg };
        var converter = Converter(options);
        var capture = SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl(), "plot.hpgl");

        var result = await converter.ConvertAsync(capture);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(Path.ChangeExtension(capture.SavedPath, "svg"), result.OutputPath);
        Assert.IsTrue(File.Exists(result.OutputPath));
        StringAssert.Contains(File.ReadAllText(result.OutputPath!), "<svg");
    }

    [TestMethod]
    public async Task ConvertAsync_InternalHpglToSvg_NonHpglCapture_Fails()
    {
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.InternalHpglToSvg };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Bmp, StreamContentSamples.Bmp()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "HP-GL");
    }

    [TestMethod]
    public async Task ConvertAsync_InternalHpglToSvg_CaptureNeverSaved_Fails()
    {
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.InternalHpglToSvg };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(UnsavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "never saved");
    }

    [TestMethod]
    public async Task ConvertAsync_ExternalTool_NoPathConfigured_Fails()
    {
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.ExternalTool };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "external converter tool");
    }

    [TestMethod]
    public async Task ConvertAsync_ExternalTool_CaptureNeverSaved_Fails()
    {
        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.ExternalTool,
            ExternalToolPath = "cmd.exe",
            ExternalToolArguments = "/c copy {input} {output}",
        };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(UnsavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "never saved");
    }

    [TestMethod]
    public async Task ConvertAsync_ExternalTool_UnknownExecutable_FailsRatherThanThrowing()
    {
        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.ExternalTool,
            ExternalToolPath = Path.Combine(_directory, "does-not-exist.exe"),
            ExternalToolArguments = "{input} {output}",
        };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public async Task ConvertAsync_ExternalTool_NonZeroExitCode_FailsWithTheExitCode()
    {
        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.ExternalTool,
            ExternalToolPath = "cmd.exe",
            ExternalToolArguments = "/c exit 7",
        };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "7");
    }

    [TestMethod]
    public async Task ConvertAsync_ExternalTool_CopiesViaPlaceholders_EvenWhenThePathContainsSpaces()
    {
        var spacedDirectory = Directory.CreateDirectory(Path.Combine(_directory, "has spaces in it")).FullName;
        var inputPath = Path.Combine(spacedDirectory, "my plot.hpgl");
        File.WriteAllBytes(inputPath, StreamContentSamples.Hpgl());
        var capture = new StreamMonitorCapture(
            new StreamCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl(), DateTimeOffset.Now, StreamCaptureEnd.Complete, WasDeclared: false),
            "unit-test-device",
            DateTimeOffset.Now,
            inputPath,
            null);

        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.ExternalTool,
            ExternalToolPath = "cmd.exe",
            ExternalToolArguments = "/c copy /y {input} {output}",
            OutputExtension = "copy",
        };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(capture);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(Path.ChangeExtension(inputPath, "copy"), result.OutputPath);
        CollectionAssert.AreEqual(StreamContentSamples.Hpgl(), File.ReadAllBytes(result.OutputPath!));
    }

    [TestMethod]
    public async Task ConvertAsync_ExternalTool_SubstitutesDpiPlaceholderAsItsOwnToken()
    {
        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.ExternalTool,
            ExternalToolPath = "cmd.exe",
            ExternalToolArguments = "/c echo {dpi}>{output}",
            ExternalToolDpi = 300,
            OutputExtension = "txt",
        };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsTrue(result.Success, result.Error);
        StringAssert.Contains(File.ReadAllText(result.OutputPath!), "300");
    }

    [TestMethod]
    public void FromCliOptions_Null_YieldsModeNone()
    {
        var options = StreamCaptureConverterOptions.FromCliOptions(null);

        Assert.AreEqual(StreamConversionMode.None, options.Mode);
    }

    [TestMethod]
    [DataRow("externaltool", StreamConversionMode.ExternalTool)]
    [DataRow("webservice", StreamConversionMode.None)]
    [DataRow("InternalHpglToSvg", StreamConversionMode.InternalHpglToSvg)]
    [DataRow("not-a-real-mode", StreamConversionMode.None)]
    [DataRow(null, StreamConversionMode.None)]
    public void FromCliOptions_ParsesModeCaseInsensitively(string? raw, StreamConversionMode expected)
    {
        var cliOptions = new CliOptions { StreamConvertMode = raw };

        var options = StreamCaptureConverterOptions.FromCliOptions(cliOptions);

        Assert.AreEqual(expected, options.Mode);
    }

    [TestMethod]
    public void FromCliOptions_CopiesEveryField()
    {
        var cliOptions = new CliOptions
        {
            StreamConvertMode = "externaltool",
            StreamConvertExternalToolPath = @"C:\tools\convert.exe",
            StreamConvertExternalToolArguments = "{input} {output}",
            StreamConvertDpi = 600,
            StreamConvertOutputExtension = "svg",
        };

        var options = StreamCaptureConverterOptions.FromCliOptions(cliOptions);

        Assert.AreEqual(StreamConversionMode.ExternalTool, options.Mode);
        Assert.AreEqual(cliOptions.StreamConvertExternalToolPath, options.ExternalToolPath);
        Assert.AreEqual(cliOptions.StreamConvertExternalToolArguments, options.ExternalToolArguments);
        Assert.AreEqual(600, options.ExternalToolDpi);
        Assert.AreEqual("svg", options.OutputExtension);
    }

    /// <summary>A minimal <see cref="HttpMessageHandler"/> stub — no fake-HTTP pattern existed yet in this repo's tests.</summary>
    private static StreamConvertToolOptions CopyTool(string name, string formats, string extension) => new()
    {
        Name = name,
        Path = "cmd.exe",
        Arguments = "/c copy /y {input} {output}",
        Formats = formats,
        OutputExtension = extension,
    };

    [TestMethod]
    public async Task ConvertAsync_Auto_PicksTheFirstToolThatHandlesTheCaptureFormat()
    {
        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.Auto,
            Tools = [CopyTool("gs", "ps", "psout"), CopyTool("gpcl", "pcl", "pclout")],
        };
        var converter = Converter(options);

        var pcl = await converter.ConvertAsync(SavedCapture(StreamContentKind.Pcl, StreamContentSamples.PjlPcl()));
        var ps = await converter.ConvertAsync(SavedCapture(StreamContentKind.PostScript, StreamContentSamples.PostScript()));

        Assert.IsTrue(pcl.Success, pcl.Error);
        Assert.IsTrue(pcl.OutputPath!.EndsWith(".pclout", StringComparison.Ordinal));
        Assert.IsTrue(ps.Success, ps.Error);
        Assert.IsTrue(ps.OutputPath!.EndsWith(".psout", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ConvertAsync_Auto_NoToolHandlesTheFormat_FailsNamingIt()
    {
        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.Auto,
            Tools = [CopyTool("gs", "ps", "psout")],
        };

        var result = await Converter(options).ConvertAsync(SavedCapture(StreamContentKind.Pcl, StreamContentSamples.PjlPcl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "PCL");
    }

    [TestMethod]
    public async Task ConvertAsync_ToolByName_RunsThatToolEvenIfItsFormatsDontMatch()
    {
        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.Tool,
            ToolName = "GS",
            Tools = [CopyTool("gs", "ps", "psout")],
        };

        var result = await Converter(options).ConvertAsync(SavedCapture(StreamContentKind.Pcl, StreamContentSamples.PjlPcl()));

        Assert.IsTrue(result.Success, result.Error);
        Assert.IsTrue(result.OutputPath!.EndsWith(".psout", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ConvertAsync_ToolByName_UnknownName_Fails()
    {
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.Tool, ToolName = "nope" };

        var result = await Converter(options).ConvertAsync(SavedCapture(StreamContentKind.Pcl, StreamContentSamples.PjlPcl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "nope");
    }

    [TestMethod]
    public void Handles_EmptyFormatsAcceptsAnything_AndTokensMatchFormatOrExtension()
    {
        Assert.IsTrue(StreamCaptureConverter.Handles(new StreamConvertToolOptions(), StreamContentKind.Bmp));
        Assert.IsTrue(StreamCaptureConverter.Handles(new StreamConvertToolOptions { Formats = "PostScript, eps" }, StreamContentKind.PostScript));
        Assert.IsTrue(StreamCaptureConverter.Handles(new StreamConvertToolOptions { Formats = "bmp" }, StreamContentKind.Bmp));
        Assert.IsTrue(StreamCaptureConverter.Handles(new StreamConvertToolOptions { Formats = "image" }, StreamContentKind.Png));
        Assert.IsFalse(StreamCaptureConverter.Handles(new StreamConvertToolOptions { Formats = "pcl" }, StreamContentKind.PostScript));
    }

    [TestMethod]
    public void CopyFrom_ReadsAutoAndToolModes_AndTheToolList()
    {
        var tools = new List<StreamConvertToolOptions> { CopyTool("gs", "ps", "png") };

        var auto = StreamCaptureConverterOptions.FromCliOptions(new CliOptions { StreamConvertMode = "auto", StreamConvertTools = tools });
        var named = StreamCaptureConverterOptions.FromCliOptions(new CliOptions { StreamConvertMode = "tool:gs", StreamConvertTools = tools });

        Assert.AreEqual(StreamConversionMode.Auto, auto.Mode);
        Assert.AreEqual(StreamConversionMode.Tool, named.Mode);
        Assert.AreEqual("gs", named.ToolName);
        Assert.HasCount(1, named.Tools);
    }

    [TestMethod]
    public void Choices_ListFixedModesThenAutoAndEachTool_AndRoundTripTheSelection()
    {
        var options = new StreamCaptureConverterOptions { Tools = [CopyTool("gs", "ps", "png"), CopyTool("gpcl", "pcl", "png")] };

        var choices = StreamConversionChoice.For(options);

        CollectionAssert.AreEqual(
            new[] { "None", "HP-GL to SVG", "Auto (by format)", "gs", "gpcl", "External tool" },
            choices.Select(c => c.DisplayName).ToArray());
        choices[4].ApplyTo(options);
        Assert.AreEqual(StreamConversionMode.Tool, options.Mode);
        Assert.AreEqual(4, StreamConversionChoice.IndexOf(choices, options));
    }

    [TestMethod]
    public void Choices_WithNoTools_AreJustTheFixedModes()
    {
        var choices = StreamConversionChoice.For(new StreamCaptureConverterOptions());

        CollectionAssert.AreEqual(new[] { "None", "HP-GL to SVG", "External tool" }, choices.Select(c => c.DisplayName).ToArray());
    }
}
