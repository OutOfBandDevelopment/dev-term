using System.Net;
using System.Net.Http;
using DevTerm.Core.StreamContent;
using DevTerm.Test.Utilities;
using Microsoft.Extensions.Options;
using Moq;

namespace DevTerm.Configuration.Tests;

/// <summary>
/// Covers <see cref="StreamCaptureConverter"/>'s three conversion mechanisms (internal HP-GL-to-SVG,
/// external tool, web service), placeholder-substitution safety, and each mechanism's failure paths.
/// See docs/design/proposals/stream-content-detection.md's "Raster/convert tool integration".
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

    private static StreamCaptureConverter Converter(StreamCaptureConverterOptions options, IHttpClientFactory? factory = null) =>
        new(Options.Create(options), factory);

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
        StringAssert.Contains(result.Error, "none");
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
    public async Task ConvertAsync_WebService_NoUrlConfigured_Fails()
    {
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.WebService };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "web-service URL");
    }

    [TestMethod]
    public async Task ConvertAsync_WebService_CaptureNeverSaved_Fails()
    {
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.WebService, WebServiceUrl = "https://example.invalid/convert" };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(UnsavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "never saved");
    }

    [TestMethod]
    public async Task ConvertAsync_WebService_SuccessResponse_WritesTheReturnedBytes()
    {
        var converted = "<svg>from the web service</svg>"u8.ToArray();
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://example.invalid/convert", request.RequestUri!.ToString());
            Assert.AreEqual(StreamContentKind.Hpgl.MediaType, request.Content!.Headers.ContentType!.MediaType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(converted) };
        });
        var factory = FakeFactory(handler);
        var options = new StreamCaptureConverterOptions
        {
            Mode = StreamConversionMode.WebService,
            WebServiceUrl = "https://example.invalid/convert",
            OutputExtension = "svg",
        };
        var converter = Converter(options, factory);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsTrue(result.Success, result.Error);
        CollectionAssert.AreEqual(converted, File.ReadAllBytes(result.OutputPath!));
    }

    [TestMethod]
    public async Task ConvertAsync_WebService_NonSuccessStatus_FailsWithTheStatusCode()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var factory = FakeFactory(handler);
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.WebService, WebServiceUrl = "https://example.invalid/convert" };
        var converter = Converter(options, factory);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "503");
    }

    [TestMethod]
    public async Task ConvertAsync_WebService_RequestThrows_FailsRatherThanPropagating()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var factory = FakeFactory(handler);
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.WebService, WebServiceUrl = "https://example.invalid/convert" };
        var converter = Converter(options, factory);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "connection refused");
    }

    [TestMethod]
    public async Task ConvertAsync_WebService_WithNoHttpClientFactory_FallsBackToAnOwnedHttpClient()
    {
        // No IHttpClientFactory supplied — covers the ad hoc (non-DI) construction path used by
        // StreamMonitorMode/StreamMonitorWindow, which still needs to fail gracefully rather than throw.
        var options = new StreamCaptureConverterOptions { Mode = StreamConversionMode.WebService, WebServiceUrl = "https://127.0.0.1:1/convert" };
        var converter = Converter(options);

        var result = await converter.ConvertAsync(SavedCapture(StreamContentKind.Hpgl, StreamContentSamples.Hpgl()));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void FromCliOptions_Null_YieldsModeNone()
    {
        var options = StreamCaptureConverterOptions.FromCliOptions(null);

        Assert.AreEqual(StreamConversionMode.None, options.Mode);
    }

    [TestMethod]
    [DataRow("externaltool", StreamConversionMode.ExternalTool)]
    [DataRow("WebService", StreamConversionMode.WebService)]
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
            StreamConvertWebServiceUrl = "https://example.invalid/convert",
            StreamConvertWebServiceMethod = "PUT",
            StreamConvertOutputExtension = "svg",
        };

        var options = StreamCaptureConverterOptions.FromCliOptions(cliOptions);

        Assert.AreEqual(StreamConversionMode.ExternalTool, options.Mode);
        Assert.AreEqual(cliOptions.StreamConvertExternalToolPath, options.ExternalToolPath);
        Assert.AreEqual(cliOptions.StreamConvertExternalToolArguments, options.ExternalToolArguments);
        Assert.AreEqual(600, options.ExternalToolDpi);
        Assert.AreEqual(cliOptions.StreamConvertWebServiceUrl, options.WebServiceUrl);
        Assert.AreEqual("PUT", options.WebServiceMethod);
        Assert.AreEqual("svg", options.OutputExtension);
    }

    private static IHttpClientFactory FakeFactory(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(StreamCaptureConverter.HttpClientName)).Returns(new HttpClient(handler));
        return factory.Object;
    }

    /// <summary>A minimal <see cref="HttpMessageHandler"/> stub — no fake-HTTP pattern existed yet in this repo's tests.</summary>
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request, cancellationToken));
    }
}
