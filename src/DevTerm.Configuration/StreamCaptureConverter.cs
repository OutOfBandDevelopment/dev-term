using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using DevTerm.Core.StreamContent;
using Microsoft.Extensions.Options;

namespace DevTerm.Configuration;

/// <summary>Which mechanism <see cref="StreamCaptureConverter"/> uses - see its own doc comment.</summary>
public enum StreamConversionMode
{
    /// <summary>No conversion mechanism is configured; <see cref="StreamCaptureConverter.ConvertAsync"/> always fails with an explanatory message.</summary>
    None,

    /// <summary>Run a configured external executable (Ghostscript-style) against the capture's saved file.</summary>
    ExternalTool,

    /// <summary>POST the capture's raw bytes to a configured HTTP endpoint and save the response body.</summary>
    WebService,

    /// <summary>dev-term's own <see cref="HpglToSvgConverter"/> - HP-GL captures only.</summary>
    InternalHpglToSvg,
}

/// <summary>
/// <see cref="StreamCaptureConverter"/>'s configuration - bound from <see cref="CliOptions"/>'s
/// <c>StreamConvert*</c> properties via <see cref="FromCliOptions"/>/<see cref="CopyFrom"/> so a
/// saved connection profile carries its Stream Monitor conversion settings the same way every other
/// per-connection setting does.
/// </summary>
public sealed class StreamCaptureConverterOptions
{
    public StreamConversionMode Mode { get; set; } = StreamConversionMode.None;

    public string? ExternalToolPath { get; set; }

    public string ExternalToolArguments { get; set; } = string.Empty;

    public int ExternalToolDpi { get; set; } = 150;

    public string? WebServiceUrl { get; set; }

    public string WebServiceMethod { get; set; } = "POST";

    public string? OutputExtension { get; set; }

    /// <summary>Builds a fresh <see cref="StreamCaptureConverterOptions"/> from <paramref name="cliOptions"/> (or all-defaults/<see cref="StreamConversionMode.None"/> when null) - for ad hoc construction outside DI (TUI/WPF, which build <see cref="StreamMonitor"/> the same ad hoc way).</summary>
    public static StreamCaptureConverterOptions FromCliOptions(CliOptions? cliOptions)
    {
        var options = new StreamCaptureConverterOptions();
        if (cliOptions is not null)
        {
            CopyFrom(cliOptions, options);
        }

        return options;
    }

    /// <summary>Copies <paramref name="cliOptions"/>'s <c>StreamConvert*</c> properties onto <paramref name="target"/> - what <see cref="ServiceCollectionExtensions.AddDevTermFrontEnd"/> configures the DI-registered options instance with.</summary>
    public static void CopyFrom(CliOptions cliOptions, StreamCaptureConverterOptions target)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        ArgumentNullException.ThrowIfNull(target);

        target.Mode = ParseMode(cliOptions.StreamConvertMode);
        target.ExternalToolPath = cliOptions.StreamConvertExternalToolPath;
        target.ExternalToolArguments = cliOptions.StreamConvertExternalToolArguments ?? string.Empty;
        target.ExternalToolDpi = cliOptions.StreamConvertDpi;
        target.WebServiceUrl = cliOptions.StreamConvertWebServiceUrl;
        target.WebServiceMethod = string.IsNullOrWhiteSpace(cliOptions.StreamConvertWebServiceMethod) ? "POST" : cliOptions.StreamConvertWebServiceMethod;
        target.OutputExtension = cliOptions.StreamConvertOutputExtension;
    }

    private static StreamConversionMode ParseMode(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "externaltool" => StreamConversionMode.ExternalTool,
        "webservice" => StreamConversionMode.WebService,
        "internalhpgltosvg" => StreamConversionMode.InternalHpglToSvg,
        _ => StreamConversionMode.None,
    };
}

/// <summary>The outcome of one <see cref="StreamCaptureConverter.ConvertAsync"/> call.</summary>
public sealed record StreamConversionResult(bool Success, string? OutputPath, string? Error);

/// <summary>
/// Wraps the three "Convert/Rasterize" mechanisms proposed in
/// docs/design/proposals/stream-content-detection.md's "Raster/convert tool integration" section —
/// external tool invocation, web-service conversion, and the internal HP-GL-to-SVG converter — behind
/// one call so the Stream Monitor windows' "Convert..." action doesn't need to know which is active.
/// </summary>
/// <remarks>
/// Never throws: every failure path (missing config, a process that fails to start or exits
/// non-zero, a failed HTTP request, malformed input) returns a <see cref="StreamConversionResult"/>
/// with <see cref="StreamConversionResult.Success"/> false and an explanatory
/// <see cref="StreamConversionResult.Error"/>, matching <see cref="StreamMonitor"/>'s own
/// tolerant, result-object-returning file I/O. <see cref="HttpClientFactory"/> is optional so this
/// can be constructed ad hoc (as <see cref="StreamMonitor"/> itself is, in the TUI/WPF front ends)
/// without a DI container; when null, each web-service conversion uses its own short-lived
/// <see cref="HttpClient"/> instead of a pooled one.
/// </remarks>
public sealed class StreamCaptureConverter
{
    /// <summary>The named <see cref="IHttpClientFactory"/> client this registers/resolves through DI.</summary>
    public const string HttpClientName = "StreamCaptureConverter";

    private readonly IOptions<StreamCaptureConverterOptions> _options;
    private readonly IHttpClientFactory? _httpClientFactory;

    public StreamCaptureConverter(IOptions<StreamCaptureConverterOptions> options, IHttpClientFactory? httpClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Whether a mechanism is configured at all - lets a caller skip offering "Convert..." when there's nothing to do.</summary>
    public bool IsConfigured => _options.Value.Mode != StreamConversionMode.None;

    /// <summary>
    /// Converts <paramref name="capture"/> per the configured mechanism, writing the result next to
    /// its already-saved file (same directory and file-name stem, <see cref="StreamCaptureConverterOptions.OutputExtension"/>
    /// or a per-mechanism default extension).
    /// </summary>
    public async Task<StreamConversionResult> ConvertAsync(StreamMonitorCapture capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var options = _options.Value;

        return options.Mode switch
        {
            StreamConversionMode.InternalHpglToSvg => ConvertInternal(capture, options),
            StreamConversionMode.ExternalTool => await ConvertExternalAsync(capture, options, cancellationToken).ConfigureAwait(false),
            StreamConversionMode.WebService => await ConvertWebServiceAsync(capture, options, cancellationToken).ConfigureAwait(false),
            _ => new StreamConversionResult(false, null, "No conversion mechanism is configured (Stream Convert Mode is 'none')."),
        };
    }

    private static StreamConversionResult ConvertInternal(StreamMonitorCapture capture, StreamCaptureConverterOptions options)
    {
        if (capture.Capture.Kind.Format != StreamContentFormat.Hpgl)
        {
            return new StreamConversionResult(false, null, $"The internal HP-GL-to-SVG converter only handles HP-GL captures, not {capture.Capture.Kind.DisplayName}.");
        }

        var outputPath = OutputPathFor(capture, options.OutputExtension ?? "svg");
        if (outputPath is null)
        {
            return new StreamConversionResult(false, null, "This capture was never saved to a file, so there is nowhere to write the converted output next to.");
        }

        try
        {
            var svg = HpglToSvgConverter.ConvertToSvg(capture.Capture.Data);
            File.WriteAllText(outputPath, svg);
            return new StreamConversionResult(true, outputPath, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StreamConversionResult(false, null, ex.Message);
        }
    }

    private static async Task<StreamConversionResult> ConvertExternalAsync(StreamMonitorCapture capture, StreamCaptureConverterOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ExternalToolPath))
        {
            return new StreamConversionResult(false, null, "No external converter tool path is configured.");
        }

        if (capture.SavedPath is not { } inputPath)
        {
            return new StreamConversionResult(false, null, "This capture was never saved to a file, so there is no input for the external tool.");
        }

        var outputPath = OutputPathFor(capture, options.OutputExtension ?? "png")!;

        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExternalToolPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // Split the raw template on whitespace, then substitute placeholders *inside* each resulting
        // token before adding it to ArgumentList - never build one shell-parsed command string. A
        // substituted path containing spaces still arrives at the process as exactly one argument,
        // the same way it would if typed with surrounding quotes in a real shell, with no quoting/
        // escaping step that a captured file name or device-supplied value could break out of.
        foreach (var rawToken in options.ExternalToolArguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken
                .Replace("{input}", inputPath, StringComparison.Ordinal)
                .Replace("{output}", outputPath, StringComparison.Ordinal)
                .Replace("{dpi}", options.ExternalToolDpi.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            startInfo.ArgumentList.Add(token);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new StreamConversionResult(false, null, $"Could not start '{options.ExternalToolPath}'.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                return new StreamConversionResult(false, null, $"'{options.ExternalToolPath}' exited with code {process.ExitCode}: {stderr.Trim()}");
            }

            if (!File.Exists(outputPath))
            {
                return new StreamConversionResult(false, null, $"'{options.ExternalToolPath}' exited successfully but did not produce '{outputPath}'.");
            }

            return new StreamConversionResult(true, outputPath, null);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new StreamConversionResult(false, null, ex.Message);
        }
    }

    private async Task<StreamConversionResult> ConvertWebServiceAsync(StreamMonitorCapture capture, StreamCaptureConverterOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.WebServiceUrl))
        {
            return new StreamConversionResult(false, null, "No web-service URL is configured.");
        }

        var outputPath = OutputPathFor(capture, options.OutputExtension ?? "png");
        if (outputPath is null)
        {
            return new StreamConversionResult(false, null, "This capture was never saved to a file, so there is nowhere to write the converted output next to.");
        }

        var ownedClient = _httpClientFactory is null ? new HttpClient() : null;
        var client = _httpClientFactory?.CreateClient(HttpClientName) ?? ownedClient!;
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(options.WebServiceMethod), options.WebServiceUrl)
            {
                Content = new ByteArrayContent(capture.Capture.Data),
            };
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(capture.Capture.Kind.MediaType);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new StreamConversionResult(false, null, $"The web service returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var converted = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(outputPath, converted, cancellationToken).ConfigureAwait(false);
            return new StreamConversionResult(true, outputPath, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return new StreamConversionResult(false, null, ex.Message);
        }
        finally
        {
            ownedClient?.Dispose();
        }
    }

    /// <summary>The converted file's path: the saved capture's own directory and file-name stem, with <paramref name="extension"/> - or null when the capture was never saved.</summary>
    private static string? OutputPathFor(StreamMonitorCapture capture, string extension) =>
        capture.SavedPath is { } savedPath
            ? Path.ChangeExtension(savedPath, extension)
            : null;
}
