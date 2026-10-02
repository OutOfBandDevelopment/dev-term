using DevTerm.Configuration;
using DevTerm.Core.StreamContent;
using DevTerm.Test.Utilities;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace DevTerm.Console.Tests;

/// <summary>
/// The TUI Stream Monitor window (<see cref="StreamMonitorMode"/>) driven headlessly against a real
/// <see cref="DevTerm.Configuration.StreamMonitor"/> fed real captures — its state line, capture
/// list, detail line and Start/Stop toggle.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
[DoNotParallelize]
public sealed class StreamMonitorModeTests
{
    [TestMethod]
    public async Task Running_WithNoCaptures_ShowsMonitoringAndWhereCapturesGo()
    {
        await using var bench = await StreamMonitorBench.StartAsync("tcp://192.168.0.5:5025");

        var dump = "";
        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = StreamMonitorMode.BuildWindow(app, bench.Monitor);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                dump = TuiTestRunner.DumpBuffer();
                Assert.AreEqual("Stop Monitoring", parts.ToggleButton.Text);
                Assert.AreEqual("Nothing captured yet.", parts.DetailLabel.Text);
            }
            finally
            {
                app.End(token);
                parts.Window.Dispose();
            }
        });

        Assert.Contains("Monitoring tcp://192.168.0.5:5025", dump);
        Assert.Contains("Saving to:", dump);
    }

    [TestMethod]
    public async Task Captures_AreListedNewestLast_WithTheSavedFileName()
    {
        await using var bench = await StreamMonitorBench.StartAsync("Bench scope");
        await bench.CaptureAsync(StreamContentSamples.Bmp());
        var png = await bench.CaptureAsync(StreamContentSamples.Png());

        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = StreamMonitorMode.BuildWindow(app, bench.Monitor);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            app.LayoutAndDraw(true);
            try
            {
                Assert.AreEqual(2, parts.CaptureList.Source!.Count);
                Assert.AreEqual(1, parts.CaptureList.SelectedItem);
                Assert.Contains($"Saved as {Path.GetFileName(png.SavedPath)} in the folder above.", parts.DetailLabel.Text);
                Assert.Contains("PNG image", parts.DetailLabel.Text);

                var dump = TuiTestRunner.DumpBuffer();
                Assert.Contains("BMP", dump);
                Assert.Contains("PNG image", dump);
                Assert.Contains("Bench_scope_20260925-", dump);
            }
            finally
            {
                app.End(token);
                parts.Window.Dispose();
            }
        });
    }

    [TestMethod]
    public async Task ToggleButton_StopsAndRestartsMonitoring()
    {
        await using var bench = await StreamMonitorBench.StartAsync("scope");

        TuiTestRunner.RunHeadlessApp(app =>
        {
            var parts = StreamMonitorMode.BuildWindow(app, bench.Monitor);
            var token = app.Begin(parts.Window) ?? throw new NotSupportedException();
            try
            {
                parts.ToggleButton.InvokeCommand(Command.Accept);
                Assert.IsFalse(bench.Monitor.IsRunning);
                Assert.AreEqual("Start Monitoring", parts.ToggleButton.Text);
                Assert.IsEmpty(bench.Session.Presenters);

                parts.ToggleButton.InvokeCommand(Command.Accept);
                Assert.IsTrue(bench.Monitor.IsRunning);
                Assert.AreEqual("Stop Monitoring", parts.ToggleButton.Text);
                Assert.HasCount(1, bench.Session.Presenters);
            }
            finally
            {
                app.End(token);
                parts.Window.Dispose();
            }
        });
    }

    [TestMethod]
    public async Task Row_DescribesKindSizeEndAndFile()
    {
        await using var bench = await StreamMonitorBench.StartAsync("scope");
        var capture = await bench.CaptureAsync(StreamContentSamples.Hpgl());

        var row = StreamMonitorMode.Row(capture);

        Assert.AreEqual(StreamCaptureEnd.IdleTimeout, capture.Capture.EndReason);
        StringAssert.Contains(row, "HPGL");
        StringAssert.Contains(row, $"{StreamContentSamples.Hpgl().Length} B");
        StringAssert.Contains(row, "went quiet");
        StringAssert.EndsWith(row, ".hpgl");
    }

    [TestMethod]
    public async Task Detail_ForAFailedSave_SaysWhy()
    {
        var notADirectory = Path.Combine(Path.GetTempPath(), "devterm-streammonitor-file-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(notADirectory, "x");
        try
        {
            await using var bench = await StreamMonitorBench.StartAsync("scope", notADirectory);
            var capture = await bench.CaptureAsync(StreamContentSamples.Bmp());

            StringAssert.Contains(StreamMonitorMode.Detail(capture), "\nNot saved: ");
            StringAssert.EndsWith(StreamMonitorMode.Row(capture), "NOT SAVED");
        }
        finally
        {
            File.Delete(notADirectory);
        }
    }

    [TestMethod]
    public async Task ConvertButton_WithInternalHpglToSvgConfigured_ConvertsTheSelectedCapture()
    {
        await using var bench = await StreamMonitorBench.StartAsync("scope");
        var capture = await bench.CaptureAsync(StreamContentSamples.Hpgl());
        var cliOptions = new CliOptions { StreamConvertMode = "internalhpgltosvg" };

        StreamMonitorWindowParts? captured = null;
        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app =>
            {
                captured = StreamMonitorMode.BuildWindow(app, bench.Monitor, cliOptions);
                return captured.Window;
            },
            body: (app, _) =>
            {
                var parts = captured!;
                TuiTestRunner.InvokeOnLoop(() =>
                {
                    parts.CaptureList.SelectedItem = 0;
                    parts.ConvertButton.InvokeCommand(Command.Accept);
                    return 0;
                });

                Assert.IsTrue(
                    TuiTestRunner.WaitUntilOnLoop(() => parts.DetailLabel.Text.Contains("Converted to", StringComparison.Ordinal), TimeSpan.FromSeconds(5)),
                    $"Expected the detail label to report a conversion; last seen: {TuiTestRunner.InvokeOnLoop(() => parts.DetailLabel.Text)}");
            });

        var expectedSvgPath = Path.ChangeExtension(capture.SavedPath, "svg");
        Assert.IsTrue(File.Exists(expectedSvgPath));
        StringAssert.Contains(File.ReadAllText(expectedSvgPath), "<svg");
    }

    [TestMethod]
    public async Task ConvertButton_WithNoModeConfigured_ReportsTheFailure()
    {
        await using var bench = await StreamMonitorBench.StartAsync("scope");
        await bench.CaptureAsync(StreamContentSamples.Hpgl());

        StreamMonitorWindowParts? captured = null;
        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app =>
            {
                captured = StreamMonitorMode.BuildWindow(app, bench.Monitor);
                return captured.Window;
            },
            body: (app, _) =>
            {
                var parts = captured!;
                TuiTestRunner.InvokeOnLoop(() =>
                {
                    parts.CaptureList.SelectedItem = 0;
                    parts.ConvertButton.InvokeCommand(Command.Accept);
                    return 0;
                });

                Assert.IsTrue(TuiTestRunner.WaitUntilOnLoop(() => parts.DetailLabel.Text.Contains("Convert failed", StringComparison.Ordinal), TimeSpan.FromSeconds(5)));
            });
    }

    [TestMethod]
    public async Task ConvertButton_AddsTheConvertedSvgToTheList()
    {
        await using var bench = await StreamMonitorBench.StartAsync("scope");
        await bench.CaptureAsync(StreamContentSamples.Hpgl());
        var cliOptions = new CliOptions { StreamConvertMode = "internalhpgltosvg" };

        StreamMonitorWindowParts? captured = null;
        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app =>
            {
                captured = StreamMonitorMode.BuildWindow(app, bench.Monitor, cliOptions);
                return captured.Window;
            },
            body: (app, _) =>
            {
                var parts = captured!;
                TuiTestRunner.InvokeOnLoop(() =>
                {
                    parts.CaptureList.SelectedItem = 0;
                    parts.ConvertButton.InvokeCommand(Command.Accept);
                    return 0;
                });

                Assert.IsTrue(
                    TuiTestRunner.WaitUntilOnLoop(() => bench.Monitor.Captures.Count == 2, TimeSpan.FromSeconds(5)),
                    "Expected the converted file to join the monitor's captures.");
                Assert.IsTrue(
                    TuiTestRunner.WaitUntilOnLoop(() => parts.CaptureList.SelectedItem == 1, TimeSpan.FromSeconds(5)),
                    "Expected the new entry to be selected.");
            });

        Assert.AreEqual("svg", bench.Monitor.Captures[1].Capture.Kind.Extension);
        Assert.AreEqual("HP-GL plot", bench.Monitor.Captures[1].ConvertedFrom);
    }

    [TestMethod]
    public async Task ModeButton_ChoosingAMode_SetsItForConvert_AndShowsItOnTheButton()
    {
        await using var bench = await StreamMonitorBench.StartAsync("scope");
        var chosenIndex = StreamConversionModes.All.ToList().IndexOf(StreamConversionMode.InternalHpglToSvg);

        StreamMonitorWindowParts? captured = null;
        TuiTestRunner.RunWithLoopApp(
            beforeBuild: null,
            build: app =>
            {
                captured = StreamMonitorMode.BuildWindow(app, bench.Monitor);
                return captured.Window;
            },
            body: (app, _) =>
            {
                var parts = captured!;
                Assert.AreEqual(StreamConversionMode.None, parts.ConverterOptions.Mode);

                TuiTestRunner.InvokeOnLoop(() =>
                {
                    app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
                    {
                        if (app.TopRunnableView is not Dialog list)
                        {
                            return true;
                        }

                        ((ListView)list.SubViews.First(v => v is ListView)).SelectedItem = chosenIndex;
                        app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                        return false;
                    });

                    parts.ModeButton.InvokeCommand(Command.Accept);
                    return 0;
                });

                Assert.AreEqual(StreamConversionMode.InternalHpglToSvg, parts.ConverterOptions.Mode);
                StringAssert.Contains(TuiTestRunner.InvokeOnLoop(() => parts.ModeButton.Text), "HP-GL to SVG");
            });
    }
}
