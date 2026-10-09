using DevTerm.Configuration;
using DevTerm.Core.StreamContent;

namespace DevTerm.Web;

/// <summary>
/// The host's one Stream Monitor (Device > Stream Monitor... in the desktop apps), watching the shared session. It follows a
/// profile switch (<see cref="SessionHub.SessionChanged"/>) and starts on the first request, like opening the desktop window.
/// </summary>
public sealed class WebStreamMonitor : IDisposable
{
    private readonly SessionHub _hub;
    private readonly ConverterToolsStore _tools;
    private readonly ConnectionProfileStore _store = new();
    private readonly object _key = new();
    private readonly ConnectionManager? _connections;

    public WebStreamMonitor(SessionHub hub, ConverterToolsStore tools, ConnectionManager? connections = null)
    {
        _hub = hub;
        _tools = tools;
        _connections = connections;
        Monitor = new StreamMonitor(watcherOptions: new StreamContentWatcherOptions { IdleTimeout = TimeSpan.FromMilliseconds(hub.Options.StreamIdleTimeoutMs) }) { AutoConvertHpgl = hub.Options.StreamAutoConvertHpgl };
        hub.SessionChanged += Retrack;
        Retrack();
        if (connections is not null)
        {
            connections.ConnectionOpened += TrackExtra;
            connections.ConnectionClosed += UntrackExtra;
            foreach (var (id, extra) in connections.OpenHubs())
            {
                TrackExtra(id, extra);
            }
        }
    }

    /// <summary>An extra connection opened from a project is watched too, under its own key, so its captures list its device name.</summary>
    private void TrackExtra(string id, SessionHub extra)
    {
        void Retrack() => Monitor.Track(id, extra.Session, StreamMonitor.DeviceNameFor(extra.Options, _store), extra.Options.EffectiveExportDirectory);
        extra.SessionChanged += Retrack;
        Retrack();
    }

    private void UntrackExtra(string id)
    {
        Monitor.Untrack(id);
    }

    public StreamMonitor Monitor { get; }

    private void Retrack() => Monitor.Track(_key, _hub.Session, StreamMonitor.DeviceNameFor(_hub.Options, _store), _hub.Options.EffectiveExportDirectory);

    /// <summary>Starts watching (a no-op when already running) and lists earlier exports the first time.</summary>
    public void Start()
    {
        if (!Monitor.IsRunning)
        {
            Monitor.Start();
        }
    }

    public void Stop() => Monitor.Stop();

    /// <summary>The Convert as list for this connection: the fixed modes plus Auto and each registered tool (the app-wide list and the profile's own).</summary>
    public IReadOnlyList<StreamConversionChoice> ConversionChoices() => StreamConversionChoice.For(ConverterOptions());

    /// <summary>The choice the connection's profile starts on.</summary>
    public int DefaultChoice() => StreamConversionChoice.IndexOf(ConversionChoices(), ConverterOptions());

    /// <summary>Runs <paramref name="choice"/> on <paramref name="capture"/>; a success is listed as a new capture beside the original.</summary>
    public async Task<StreamConversionResult> ConvertAsync(StreamMonitorCapture capture, StreamConversionChoice choice)
    {
        var options = ConverterOptions();
        choice.ApplyTo(options);
        var result = await new StreamCaptureConverter(Microsoft.Extensions.Options.Options.Create(options)).ConvertAsync(capture).ConfigureAwait(false);
        if (result.Success && result.OutputPath is not null)
        {
            Monitor.AddConverted(capture, result.OutputPath);
        }

        return result;
    }

    private StreamCaptureConverterOptions ConverterOptions() => StreamCaptureConverterOptions.FromCliOptions(_hub.Options, _tools.Load());

    public void Dispose()
    {
        _hub.SessionChanged -= Retrack;
        if (_connections is not null)
        {
            _connections.ConnectionOpened -= TrackExtra;
            _connections.ConnectionClosed -= UntrackExtra;
        }

        Monitor.Dispose();
    }
}
