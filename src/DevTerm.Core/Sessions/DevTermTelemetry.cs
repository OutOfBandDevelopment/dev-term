using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DevTerm.Core.Sessions;

/// <summary>
/// dev-term's own diagnostics: one <see cref="ActivitySource"/> and one <see cref="Meter"/>, both named
/// <see cref="Name"/>. They cost next to nothing with no listener, so <see cref="Session"/> always records into
/// them; <c>DevTerm.Observability</c> subscribes an OpenTelemetry exporter when the user opts in. See
/// docs/design/observability.md.
/// </summary>
public static class DevTermTelemetry
{
    /// <summary>The source and meter name an exporter subscribes to.</summary>
    public const string Name = "DevTerm";

    internal static readonly ActivitySource Source = new(Name);

    private static readonly Meter _meter = new(Name);

    internal static readonly Counter<long> SessionsOpened = _meter.CreateCounter<long>("devterm.session.opened", description: "Connections opened.");

    internal static readonly Counter<long> SessionsClosed = _meter.CreateCounter<long>("devterm.session.closed", description: "Connections closed; tagged by whether the user asked and by the error type.");

    internal static readonly Counter<long> BytesSent = _meter.CreateCounter<long>("devterm.bytes.sent", "By", "Bytes written to the device.");

    internal static readonly Counter<long> BytesReceived = _meter.CreateCounter<long>("devterm.bytes.received", "By", "Bytes read from the device.");
}
