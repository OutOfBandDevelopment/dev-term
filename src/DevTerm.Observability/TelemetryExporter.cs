using DevTerm.Core.Sessions;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DevTerm.Observability;

/// <summary>Sends <see cref="DevTermTelemetry"/>'s traces and metrics to an OTLP collector (gRPC) until disposed.</summary>
public sealed class TelemetryExporter : IDisposable
{
    private readonly TracerProvider _tracer;
    private readonly MeterProvider _meter;

    private TelemetryExporter(TracerProvider tracer, MeterProvider meter)
    {
        _tracer = tracer;
        _meter = meter;
    }

    /// <summary>Parses an OTLP endpoint (<c>true</c> means the usual local collector, <c>http://localhost:4317</c>); null when it isn't an absolute http(s) URL.</summary>
    public static Uri? ParseEndpoint(string? text)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("http://localhost:4317");
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }

    /// <summary>Starts exporting to <paramref name="endpoint"/>. Exporting is asynchronous and best effort: an unreachable collector never throws here or slows the app.</summary>
    public static TelemetryExporter Start(Uri endpoint, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var resource = ResourceBuilder.CreateDefault().AddService(serviceName);

        var tracer = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(DevTermTelemetry.Name)
            .AddOtlpExporter(o =>
            {
                o.Endpoint = endpoint;
                o.Protocol = OtlpExportProtocol.Grpc;
            })
            .Build();
        var meter = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(DevTermTelemetry.Name)
            .AddOtlpExporter(o =>
            {
                o.Endpoint = endpoint;
                o.Protocol = OtlpExportProtocol.Grpc;
            })
            .Build();
        return new TelemetryExporter(tracer, meter);
    }

    public void Dispose()
    {
        _tracer.Dispose();
        _meter.Dispose();
    }
}
