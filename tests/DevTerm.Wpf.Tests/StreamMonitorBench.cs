using System.IO;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.StreamContent;
using Microsoft.Extensions.Time.Testing;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// A running <see cref="StreamMonitor"/> over a real <see cref="Session"/> (behind a
/// <see cref="FakeTransport"/>), on a <see cref="FakeTimeProvider"/> so capture timestamps and file
/// names are deterministic, saving into a throwaway export directory — for driving the Stream
/// Monitor window with real captures in tests and screenshots.
/// </summary>
internal sealed class StreamMonitorBench : IAsyncDisposable
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    private StreamMonitorBench(string exportDirectory)
    {
        ExportDirectory = exportDirectory;
        Transport = new FakeTransport();
        Session = new Session(Transport, new Pipeline([]));
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 14, 35, 12, TimeSpan.Zero));
        Monitor = new StreamMonitor(Time, new StreamContentWatcherOptions { IdleTimeout = TimeSpan.FromSeconds(2) });
    }

    public string ExportDirectory { get; }

    public FakeTransport Transport { get; }

    public Session Session { get; }

    public FakeTimeProvider Time { get; }

    public StreamMonitor Monitor { get; }

    public static async Task<StreamMonitorBench> StartAsync(string deviceName, string? exportDirectory = null, bool start = true)
    {
        var bench = new StreamMonitorBench(exportDirectory ?? Path.Combine(Path.GetTempPath(), "devterm-streammonitor-" + Guid.NewGuid().ToString("N")));
        bench.Monitor.SetSession(bench.Session, deviceName, bench.ExportDirectory);
        if (start)
        {
            bench.Monitor.Start();
        }

        await bench.Session.OpenAsync();
        return bench;
    }

    /// <summary>
    /// Plays <paramref name="data"/> as incoming device bytes and waits for the capture it produces.
    /// A format with no in-band end (HP-GL, TIFF) only completes on idle, so time is advanced past
    /// the idle timeout until it does.
    /// </summary>
    public async Task<StreamMonitorCapture> CaptureAsync(byte[] data)
    {
        var next = new TaskCompletionSource<StreamMonitorCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, StreamMonitorCapture capture) => next.TrySetResult(capture);
        Monitor.CaptureAdded += Handler;
        try
        {
            await Transport.PushIncomingAsync(data);
            var deadline = DateTime.UtcNow + _waitTimeout;
            while (!next.Task.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("No capture was produced.");
                }

                await Task.Delay(20);
                if (!next.Task.IsCompleted)
                {
                    Time.Advance(TimeSpan.FromSeconds(2));
                }
            }

            return await next.Task;
        }
        finally
        {
            Monitor.CaptureAdded -= Handler;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Monitor.Dispose();
        await Session.DisposeAsync();
        if (Directory.Exists(ExportDirectory))
        {
            Directory.Delete(ExportDirectory, recursive: true);
        }
    }
}
