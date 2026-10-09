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
    private readonly ConnectionProfileStore _store = new();
    private readonly object _key = new();

    public WebStreamMonitor(SessionHub hub)
    {
        _hub = hub;
        Monitor = new StreamMonitor(watcherOptions: new StreamContentWatcherOptions { IdleTimeout = TimeSpan.FromMilliseconds(hub.Options.StreamIdleTimeoutMs) }) { AutoConvertHpgl = hub.Options.StreamAutoConvertHpgl };
        hub.SessionChanged += Retrack;
        Retrack();
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

    public void Dispose()
    {
        _hub.SessionChanged -= Retrack;
        Monitor.Dispose();
    }
}
