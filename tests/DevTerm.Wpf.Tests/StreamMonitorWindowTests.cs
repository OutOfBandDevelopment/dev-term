using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.StreamContent;
using DevTerm.Presenters.Text;
using DevTerm.Test.Utilities;

namespace DevTerm.Wpf.Tests;

/// <summary>
/// <see cref="StreamMonitorWindow"/> against a real <see cref="StreamMonitor"/> fed real captures
/// through a live session: the capture list, the native-image preview (a real PNG WPF encodes and
/// then decodes), the "preview not available yet" path for HP-GL, a truncated image, and the
/// Start/Stop toggle — plus <see cref="MainWindow"/>'s own wiring (status lines per capture, and
/// the monitor following a live profile switch). Never <c>Show()</c>s a window (see CLAUDE.md).
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class StreamMonitorWindowTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public required TestContext TestContext { get; set; }

    /// <summary>A real PNG of a small scope-style screen (grid plus a sine trace), encoded by WPF itself — what a screen-dump query would plausibly return.</summary>
    internal static byte[] ScopeScreenPng(int width = 480, int height = 272)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 16, 28)), null, new Rect(0, 0, width, height));
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(50, 60, 80)), 1);
            for (var x = 0; x <= width; x += width / 10)
            {
                context.DrawLine(gridPen, new Point(x, 0), new Point(x, height));
            }

            for (var y = 0; y <= height; y += height / 8)
            {
                context.DrawLine(gridPen, new Point(0, y), new Point(width, y));
            }

            var trace = new StreamGeometry();
            using (var geometry = trace.Open())
            {
                geometry.BeginFigure(new Point(0, height / 2.0), isFilled: false, isClosed: false);
                for (var x = 1; x <= width; x++)
                {
                    geometry.LineTo(new Point(x, (height / 2.0) - (Math.Sin(x / (double)width * Math.PI * 6) * height * 0.3)), isStroked: true, isSmoothJoin: true);
                }
            }

            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(250, 210, 60)), 2), trace);
            var label = new FormattedText("CH1  1.000 kHz  2.00 Vpp", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 14, Brushes.White, 1.0);
            context.DrawText(label, new Point(8, 6));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [TestMethod]
    public void NativeImageCapture_IsListedSelectedAndPreviewed()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("Bench scope");
            await bench.CaptureAsync(StreamContentSamples.Hpgl());
            await bench.CaptureAsync(ScopeScreenPng());

            var window = new StreamMonitorWindow(bench.Monitor);
            StaTestRunner.DoEvents();

            Assert.HasCount(2, window.Items);
            Assert.AreEqual(1, window.CaptureList.SelectedIndex);
            var preview = window.PreviewImage.Source as BitmapSource;
            Assert.IsNotNull(preview, "Expected the PNG to be decoded into a live preview.");
            Assert.AreEqual(480, preview.PixelWidth);
            Assert.AreEqual(string.Empty, window.PreviewMessage.Text);
            Assert.IsTrue(window.ExportAsButton.IsEnabled);
            Assert.Contains("PNG image", window.DetailText.Text);
            Assert.Contains("Saved to ", window.SavedPathText.Text);
            Assert.Contains("Monitoring Bench scope", window.StateText.Text);
            window.Close();
        });
    }

    [TestMethod]
    public void FilterSearchAndSort_NarrowTheOneList()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("Bench scope");
            await bench.CaptureAsync(StreamContentSamples.Hpgl());
            await bench.CaptureAsync(ScopeScreenPng());

            var window = new StreamMonitorWindow(bench.Monitor);
            StaTestRunner.DoEvents();
            var (search, kind, device, sort) = window.Criteria;
            Assert.HasCount(2, window.Items);
            Assert.Contains("PNG image", kind.Items.Cast<string>().ToList());

            kind.SelectedItem = "PNG image";
            StaTestRunner.DoEvents();
            Assert.HasCount(1, window.Items);
            Assert.AreEqual("PNG image", window.Items[0].Capture.Capture.Kind.DisplayName);

            kind.SelectedIndex = 0;
            search.Text = "hp-gl";
            StaTestRunner.DoEvents();
            Assert.HasCount(1, window.Items);
            Assert.AreEqual("HP-GL plot", window.Items[0].Capture.Capture.Kind.DisplayName);

            search.Text = "no such thing";
            StaTestRunner.DoEvents();
            Assert.IsEmpty(window.Items);
            Assert.IsFalse(window.ExportAsButton.IsEnabled);

            search.Text = string.Empty;
            sort.SelectedItem = StreamCaptureSort.Largest;
            StaTestRunner.DoEvents();
            Assert.HasCount(2, window.Items);
            Assert.IsGreaterThanOrEqualTo(window.Items[1].Capture.Capture.Data.Length, window.Items[0].Capture.Capture.Data.Length);
            Assert.AreEqual(1, device.Items.Count - 1, "One device, so one choice besides 'All devices'.");
            window.Close();
        });
    }

    [TestMethod]
    public void HpglCapture_SaysPreviewIsNotAvailableYet()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("plotter");
            await bench.CaptureAsync(StreamContentSamples.Hpgl());

            var window = new StreamMonitorWindow(bench.Monitor);
            StaTestRunner.DoEvents();

            Assert.IsNull(window.PreviewImage.Source);
            Assert.Contains("Preview not available yet for HP-GL plot", window.PreviewMessage.Text);
            window.Close();
        });
    }

    [TestMethod]
    public void TryDecode_BytesThatOnlyLookLikeAnImage_ReturnsTheDecodersReason()
    {
        StaTestRunner.Run(() =>
        {
            byte[] garbage = [.. StreamContentSamples.Png()[..8], .. Enumerable.Repeat((byte)0x5A, 40)];

            Assert.IsNull(StreamMonitorWindow.TryDecode(garbage, out var error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void MonitoringStoppedMidImage_ListsTheFlushedCapture()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("scope");
            await bench.Transport.PushIncomingAsync(ScopeScreenPng()[..200]);
            await Task.Delay(100, TestContext.CancellationToken);
            bench.Monitor.Stop();

            var window = new StreamMonitorWindow(bench.Monitor);
            StaTestRunner.DoEvents();

            Assert.HasCount(1, window.Items);
            Assert.AreEqual(StreamCaptureEnd.Flushed, window.Items[0].Capture.Capture.EndReason);
            Assert.Contains("PNG image, 200 bytes, stopped", window.DetailText.Text);
            Assert.Contains("Stopped", window.StateText.Text);
            Assert.AreEqual("Start Monitoring", window.ToggleButton.Content);
            window.Close();
        });
    }

    [TestMethod]
    public void EmptyMonitor_ShowsTheNothingCapturedMessage()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("scope");

            var window = new StreamMonitorWindow(bench.Monitor);
            StaTestRunner.DoEvents();

            Assert.IsEmpty(window.Items);
            Assert.StartsWith("Nothing captured yet", window.PreviewMessage.Text);
            Assert.IsFalse(window.ExportAsButton.IsEnabled);
            window.Close();
        });
    }

    [TestMethod]
    public void CaptureArrivingWhileOpen_IsAddedAndSelected()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("scope");
            var window = new StreamMonitorWindow(bench.Monitor);
            StaTestRunner.DoEvents();

            await bench.CaptureAsync(StreamContentSamples.Bmp());

            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.Items.Count == 1, _timeout));
            Assert.AreEqual(0, window.CaptureList.SelectedIndex);
            Assert.IsNotNull(window.PreviewImage.Source, "The 2x2 BMP sample decodes natively.");
            window.Close();
        });
    }

    [TestMethod]
    public void ToggleMonitoring_StopsAndRestarts()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("scope");
            var window = new StreamMonitorWindow(bench.Monitor);

            window.ToggleMonitoring();
            Assert.IsFalse(bench.Monitor.IsRunning);
            Assert.AreEqual("Start Monitoring", window.ToggleButton.Content);
            Assert.IsEmpty(bench.Session.Presenters);

            window.ToggleMonitoring();
            Assert.IsTrue(bench.Monitor.IsRunning);
            Assert.AreEqual("Stop Monitoring", window.ToggleButton.Content);
            window.Close();
        });
    }

    [TestMethod]
    public void ConvertButton_IsEnabledOnlyWhenACaptureIsSelected()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("scope");
            var window = new StreamMonitorWindow(bench.Monitor);
            StaTestRunner.DoEvents();

            Assert.IsFalse(window.ConvertButton.IsEnabled);

            await bench.CaptureAsync(StreamContentSamples.Hpgl());
            Assert.IsTrue(StaTestRunner.PumpUntil(() => window.Items.Count == 1, _timeout));
            Assert.IsTrue(window.ConvertButton.IsEnabled);
            window.Close();
        });
    }

    [TestMethod]
    public void ConvertSelectedAsync_WithInternalHpglToSvgConfigured_ConvertsTheSelectedCapture()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("plotter");
            var capture = await bench.CaptureAsync(StreamContentSamples.Hpgl());

            var window = new StreamMonitorWindow(bench.Monitor, new CliOptions { StreamConvertMode = "internalhpgltosvg" });
            StaTestRunner.DoEvents();

            await window.ConvertSelectedAsync();

            var expectedSvgPath = Path.ChangeExtension(capture.SavedPath, "svg");
            Assert.IsTrue(File.Exists(expectedSvgPath));
            StringAssert.Contains(File.ReadAllText(expectedSvgPath), "<svg");
            Assert.Contains("Converted to", window.DetailText.Text);
            window.Close();
        });
    }

    [TestMethod]
    public void ConvertSelectedAsync_AddsTheSvgToTheList_AndDrawsIt()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("plotter");
            await bench.CaptureAsync(StreamContentSamples.Hpgl());

            var window = new StreamMonitorWindow(bench.Monitor, new CliOptions { StreamConvertMode = "internalhpgltosvg" });
            StaTestRunner.DoEvents();
            Assert.HasCount(1, window.Items);

            await window.ConvertSelectedAsync();
            StaTestRunner.DoEvents();

            Assert.HasCount(2, window.Items);
            var added = window.Items[1].Capture;
            Assert.IsTrue(added.Capture.Kind.IsSvg);
            Assert.AreEqual("HP-GL plot", added.ConvertedFrom);
            Assert.AreSame(window.Items[1], window.CaptureList.SelectedItem);
            Assert.IsNotNull(window.PreviewImage.Source);
            Assert.AreEqual(string.Empty, window.PreviewMessage.Text);
            window.Close();
        });
    }

    [TestMethod]
    public void SvgPreview_DrawsTheSupportedShapes_AndRejectsNonSvg()
    {
        StaTestRunner.Run(() =>
        {
            var image = SvgPreview.TryRender(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 50 20\"><path d=\"M 1,1 L 40,10\" fill=\"none\" stroke=\"red\"/><circle cx=\"10\" cy=\"10\" r=\"4\" fill=\"blue\"/></svg>",
                out var error);
            Assert.IsNotNull(image, error);
            Assert.AreEqual(50, image.Width);
            Assert.AreEqual(20, image.Height);

            Assert.IsNull(SvgPreview.TryRender("<html/>", out var notSvg));
            Assert.IsNotNull(notSvg);
            Assert.IsNull(SvgPreview.TryRender("not xml", out var bad));
            Assert.IsNotNull(bad);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ConvertModeBox_ListsEveryMode_StartsOnTheProfilesMode_AndChoosingOneConverts()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("plotter");
            var capture = await bench.CaptureAsync(StreamContentSamples.Hpgl());

            var window = new StreamMonitorWindow(bench.Monitor);
            StaTestRunner.DoEvents();

            Assert.AreEqual(StreamConversionModes.All.Count, window.ConvertModeBox.Items.Count);
            Assert.AreEqual(StreamConversionMode.None, window.ConvertMode);

            window.ConvertMode = StreamConversionMode.InternalHpglToSvg;
            await window.ConvertSelectedAsync();

            Assert.IsTrue(File.Exists(Path.ChangeExtension(capture.SavedPath, "svg")));
            Assert.Contains("Converted to", window.DetailText.Text);
            window.Close();
        });
    }

    [TestMethod]
    public void ConvertModeBox_OffersTheAppWideConverterTools()
    {
        StaTestRunner.Run(async () =>
        {
            await using var bench = await StreamMonitorBench.StartAsync("plotter");
            var withoutTools = new StreamMonitorWindow(bench.Monitor);
            var window = new StreamMonitorWindow(bench.Monitor, null, [new StreamConvertToolOptions { Name = "gs", Path = "gs.exe" }]);
            StaTestRunner.DoEvents();

            // The first registered tool adds Auto and the tool itself.
            Assert.AreEqual(withoutTools.ConvertModeBox.Items.Count + 2, window.ConvertModeBox.Items.Count);
            Assert.IsTrue(window.ConvertModeBox.Items.Cast<ComboBoxItem>().Any(i => i.Content?.ToString()?.Contains("gs", StringComparison.Ordinal) == true));
            withoutTools.Close();
            window.Close();
        });
    }

    [TestMethod]
    public void MainWindow_StreamMonitor_ReportsEachCaptureAsAStatusLine_AndFollowsAProfileSwitch()
    {
        var exportDirectory = Path.Combine(Path.GetTempPath(), "devterm-wpf-streammonitor-" + Guid.NewGuid().ToString("N"));
        try
        {
            StaTestRunner.Run(async () =>
            {
                var transport = new FakeTransport();
                var presenter = new AsciiPresenter(Microsoft.Extensions.Options.Options.Create(new AsciiPresenterOptions()));
                var session = new Session(transport, new Pipeline([presenter]));
                var window = new MainWindow(
                    session,
                    new PresenterCatalog([presenter]),
                    new CliOptions { Transport = "tcp", Host = "127.0.0.1", Port = "1", Parser = "ascii", ExportDirectory = exportDirectory },
                    IsolatedProfiles.Empty())
                {
                    ShowInTaskbar = false,
                };
                await window.ConnectAsync();

                var monitor = window.EnsureStreamMonitor();
                Assert.IsTrue(monitor.IsRunning);
                Assert.AreEqual("tcp://127.0.0.1:1", monitor.DeviceName);
                Assert.Contains(p => p is StreamContentWatcher, session.Presenters);

                await transport.PushIncomingAsync(StreamContentSamples.Bmp());
                Assert.IsTrue(
                    StaTestRunner.PumpUntil(() => window.OutputList.Items.Cast<object>().Any(i => i.ToString()!.Contains("Captured 70 bytes of BMP image", StringComparison.Ordinal)), _timeout),
                    "Expected a status line for the capture in the main window's output.");
                Assert.HasCount(1, Directory.GetFiles(exportDirectory, "tcp_127.0.0.1_1_*.bmp"));

                var switched = await window.SwitchProfileAsync(new CliOptions { Transport = "loopback", Presenter = ["ascii"], ExportDirectory = exportDirectory });

                Assert.IsTrue(switched);
                Assert.AreSame(monitor, window.EnsureStreamMonitor());
                Assert.IsTrue(monitor.IsRunning);
                Assert.AreEqual("loopback://", monitor.DeviceName);
                Assert.DoesNotContain(p => p is StreamContentWatcher, session.Presenters);

                var closed = false;
                window.Closed += (_, _) => closed = true;
                window.Close();
                Assert.IsTrue(StaTestRunner.PumpUntil(() => closed, _timeout));
                Assert.IsFalse(monitor.IsRunning, "Closing the main window stops (disposes) its monitor.");
            });
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }
}
