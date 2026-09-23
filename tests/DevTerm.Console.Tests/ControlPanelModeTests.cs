using System.Buffers;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.UiDefinitions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

namespace DevTerm.Console.Tests;

/// <summary>
/// Drives a real <see cref="ControlPanelMode"/> window headlessly, the same conventions
/// <see cref="ConfigureModeTests"/> established: a synthetic <see cref="UiDefinition"/> (not the
/// K8055's own, since this renderer is meant to be generic — see
/// <see cref="DevTerm.Devices.K8055.K8055UiDefinition"/> for the real one) plus a fake
/// <see cref="IControlSurface"/>/<see cref="IStructuredPresenter"/> stand in for a real device.
/// </summary>
[TestCategory("UNIT")]
[TestClass]
[DoNotParallelize]
public sealed class ControlPanelModeTests
{
    private sealed class FakeControlSurface : IControlSurface
    {
        public List<(string CommandId, string? Value)> Invocations { get; } = [];

        public Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default)
        {
            Invocations.Add((commandId, value));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStructuredPresenter : IPresenter, IStructuredPresenter
    {
        public string Name => "fake";

        public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) => [];

        public event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;

        public void Fire(IReadOnlyDictionary<string, string> values) => ValuesChanged?.Invoke(this, values);
    }

    private static UiDefinition BuildSampleDefinition() => new()
    {
        Name = "Sample Device",
        Sections =
        [
            new UiSection
            {
                Label = "Outputs",
                Controls =
                [
                    new ButtonControl { Id = "reset", Label = "Reset" },
                    new ToggleControl { Id = "toggle1", Label = "Toggle 1", DefaultValue = false },
                    new SliderControl { Id = "slider1", Label = "Slider 1", Minimum = 0, Maximum = 255, DefaultValue = 10 },
                    new NumericControl { Id = "numeric1", Label = "Numeric 1", Minimum = 0, Maximum = 100, DefaultValue = 5 },
                    new ChoiceControl { Id = "choice1", Label = "Choice 1", Options = ["a", "b"], DefaultValue = "a" },
                    new TextFieldControl { Id = "text1", Label = "Text 1", DefaultValue = "hi" },
                ],
            },
            new UiSection
            {
                Label = "Inputs",
                Controls = [new IndicatorControl { Id = "indicator1", Label = "Indicator 1", DefaultValue = "0" }],
            },
        ],
    };

    private static void RunHeadless(IControlSurface surface, IPresenter? structuredSource, Action<ControlPanelWindowParts> body)
    {
        Application.Init("dotnet");
        try
        {
            var parts = ControlPanelMode.BuildWindow(BuildSampleDefinition(), surface, structuredSource, "Test Panel");
            var token = Application.Begin(parts.Window);
            Application.LayoutAndDraw(true);

            try
            {
                body(parts);
            }
            finally
            {
                Application.End(token);
            }
        }
        finally
        {
            Application.Shutdown();
        }
    }

    // Same InvokeCommand(Command.Accept) mechanism ConfigureModeTests.Click uses for Button — and
    // it doubles as the way to commit a TextField-backed Slider/Numeric/TextField control here,
    // since ControlPanelMode wires their commit logic to each field's own Accepting event rather
    // than a separate Apply button.
    private static void Accept(View view) => view.InvokeCommand(Command.Accept);

    [TestMethod]
    public void BuildWindow_RendersEachControlKindAsTheExpectedViewType()
    {
        RunHeadless(new FakeControlSurface(), null, parts =>
        {
            Assert.IsInstanceOfType<Button>(parts.ControlViews["reset"]);
            Assert.IsInstanceOfType<CheckBox>(parts.ControlViews["toggle1"]);
            Assert.IsInstanceOfType<TextField>(parts.ControlViews["slider1"]);
            Assert.IsInstanceOfType<TextField>(parts.ControlViews["numeric1"]);
            Assert.IsInstanceOfType<OptionSelector>(parts.ControlViews["choice1"]);
            Assert.IsInstanceOfType<TextField>(parts.ControlViews["text1"]);
            Assert.IsInstanceOfType<Label>(parts.ControlViews["indicator1"]);
            Assert.IsTrue(parts.IndicatorLabels.ContainsKey("indicator1"));
        });
    }

    [TestMethod]
    public void Button_WhenClicked_InvokesItsIdWithNoValue()
    {
        var surface = new FakeControlSurface();
        RunHeadless(surface, null, parts =>
        {
            Accept(parts.ControlViews["reset"]);

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("reset", (string?)null), surface.Invocations[0]);
        });
    }

    [TestMethod]
    public void Toggle_WhenChecked_InvokesItsIdWithOne()
    {
        var surface = new FakeControlSurface();
        RunHeadless(surface, null, parts =>
        {
            ((CheckBox)parts.ControlViews["toggle1"]).Value = CheckState.Checked;

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("toggle1", "1"), surface.Invocations[0]);
        });
    }

    [TestMethod]
    public void Slider_WhenCommitted_ClampsAndInvokesItsIdWithTheValue()
    {
        var surface = new FakeControlSurface();
        RunHeadless(surface, null, parts =>
        {
            var field = (TextField)parts.ControlViews["slider1"];
            field.Text = "999"; // above Maximum=255, should clamp
            Accept(field);

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("slider1", "255"), surface.Invocations[0]);
            Assert.AreEqual("255", field.Text.ToString());
        });
    }

    [TestMethod]
    public void Numeric_WhenCommitted_InvokesItsIdWithTheValue()
    {
        var surface = new FakeControlSurface();
        RunHeadless(surface, null, parts =>
        {
            var field = (TextField)parts.ControlViews["numeric1"];
            field.Text = "42";
            Accept(field);

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("numeric1", "42"), surface.Invocations[0]);
        });
    }

    [TestMethod]
    public void Choice_WhenSelectionChanges_InvokesItsIdWithTheSelectedOption()
    {
        var surface = new FakeControlSurface();
        RunHeadless(surface, null, parts =>
        {
            ((OptionSelector)parts.ControlViews["choice1"]).Value = 1; // "b"

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("choice1", "b"), surface.Invocations[0]);
        });
    }

    [TestMethod]
    public void TextField_WhenCommitted_InvokesItsIdWithTheTypedValue()
    {
        var surface = new FakeControlSurface();
        RunHeadless(surface, null, parts =>
        {
            var field = (TextField)parts.ControlViews["text1"];
            field.Text = "hello";
            Accept(field);

            Assert.HasCount(1, surface.Invocations);
            Assert.AreEqual(("text1", "hello"), surface.Invocations[0]);
        });
    }

    [TestMethod]
    public void NoStructuredPresenter_ShowsTheNotDecodingStatusLine()
    {
        RunHeadless(new FakeControlSurface(), null, _ =>
        {
            StringAssert.Contains(TuiTestRunner.DumpBuffer(), "Not decoding");
        });
    }

    [TestMethod]
    public void ValuesChanged_UpdatesTheMatchingIndicatorLabel()
    {
        // Application.Invoke (which the production ValuesChanged handler marshals through) only
        // ever flushes while a real Application.Run() loop is actively pumping - RunHeadless alone
        // never drains it, per TuiTestRunner's own documented finding. This needs a real loop.
        var surface = new FakeControlSurface();
        var presenter = new FakeStructuredPresenter();

        TuiWindowPartsHarness.RunWithLoop(surface, presenter, parts =>
        {
            presenter.Fire(new Dictionary<string, string> { ["indicator1"] = "42" });

            var updated = TuiWindowPartsHarness.WaitUntil(
                () => parts.IndicatorLabels["indicator1"].Text.ToString() == "42",
                TimeSpan.FromSeconds(5));
            Assert.IsTrue(updated, "The indicator label was never updated from ValuesChanged.");
        });
    }

    /// <summary>
    /// A minimal <c>Application.Run()</c>-loop harness for <see cref="ControlPanelMode.BuildWindow"/>,
    /// analogous to <see cref="TuiTestRunner.RunWithLoop"/> but built against this renderer's own
    /// <c>(UiDefinition, IControlSurface, IPresenter?, string)</c> signature rather than
    /// <c>TuiMode.BuildWindow</c>'s.
    /// </summary>
    private static class TuiWindowPartsHarness
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(5);

        public static void RunWithLoop(IControlSurface surface, IPresenter structuredSource, Action<ControlPanelWindowParts> body)
        {
            ControlPanelWindowParts? parts = null;
            var ready = new ManualResetEventSlim(false);
            Exception? threadException = null;

            var thread = new Thread(() =>
            {
                try
                {
                    Application.Init("dotnet");
                    parts = ControlPanelMode.BuildWindow(BuildSampleDefinition(), surface, structuredSource, "Test Panel");
                    Application.Invoke(() => ready.Set());
                    Application.Run(parts.Window);
                }
                catch (Exception ex)
                {
                    threadException = ex;
                    ready.Set();
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();

            if (!ready.Wait(StartTimeout))
            {
                throw new TimeoutException("The TUI run loop did not start in time.");
            }

            if (threadException is not null)
            {
                throw new InvalidOperationException("The TUI run loop failed to start.", threadException);
            }

            try
            {
                body(parts!);
            }
            finally
            {
                Application.Invoke(() => Application.RequestStop());
                thread.Join(StopTimeout);
                Application.Shutdown();
            }
        }

        public static bool WaitUntil(Func<bool> predicate, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var done = new ManualResetEventSlim(false);
                var result = false;
                Application.Invoke(() =>
                {
                    result = predicate();
                    done.Set();
                });

                if (!done.Wait(InvokeTimeout))
                {
                    throw new TimeoutException("Application.Invoke did not run within the timeout.");
                }

                if (result)
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            return false;
        }
    }
}
