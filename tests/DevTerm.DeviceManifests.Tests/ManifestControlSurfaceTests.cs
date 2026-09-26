using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Test.Utilities;
using DevTerm.Transports.Loopback;
using DevTerm.UiDefinitions;
using Microsoft.Extensions.Options;

namespace DevTerm.DeviceManifests.Tests;

/// <summary>
/// A loaded manifest turned into a live panel (<see cref="ManifestPanel"/>): its
/// <see cref="ManifestControlSurface"/> executing command templates over a real <see cref="Session"/>
/// (a <see cref="FakeTransport"/> for exact wire bytes, the real loopback transport for the bundled
/// example manifest end to end), and its <see cref="ManifestReplyPresenter"/> correlating replies and
/// publishing response-pattern captures.
/// </summary>
[TestCategory(TestCategories.Unit)]
[TestClass]
public sealed class ManifestControlSurfaceTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public required TestContext TestContext { get; set; }

    private static DeviceManifest BuildPowerSupply() => new()
    {
        Name = "Test Supply",
        Terminator = "\n",
        Inbound = new InboundProtocol
        {
            Patterns = [new ResponsePattern { Name = "reading", Match = @"^V=(?<volts>[-\d.]+) I=(?<amps>[-\d.]+)$" }],
        },
        OutboundCommands =
        [
            new OutboundCommand { Id = "read", Name = "Read", Template = "READ?", IsQuery = true },
            new OutboundCommand
            {
                Id = "vset",
                Name = "Set Voltage",
                Template = "VSET1:{volts}",
                Parameters = [new CommandParameter { Name = "volts", Type = "number", Minimum = 0, Maximum = 30, Format = "00.00", DefaultValue = "5" }],
            },
            new OutboundCommand
            {
                Id = "ch",
                Name = "Channel Level",
                Template = "CH{channel}:{level}",
                Parameters =
                [
                    new CommandParameter { Name = "channel", Type = "integer", Minimum = 1, Maximum = 4 },
                    new CommandParameter { Name = "level", Type = "string", DefaultValue = "LOW" },
                ],
            },
            new OutboundCommand { Id = "out", Name = "Output", Template = "OUT{value}" },
        ],
        Ui = new UiDefinition
        {
            Name = "Test Supply",
            Sections =
            [
                new UiSection
                {
                    Label = "Output",
                    Controls =
                    [
                        new TextFieldControl { Id = "vset.volts", Label = "Volts" },
                        new ButtonControl { Id = "vset.send", Label = "Set", CommandId = "vset", ParameterFieldIds = ["vset.volts"] },
                        new ToggleControl { Id = "out", Label = "Output" },
                        new IndicatorControl { Id = "read.reply", Label = "Reply" },
                        new BarGraphControl { Id = "levels", Label = "Levels", Channels = [new ChartChannel { Id = "volts" }] },
                    ],
                },
            ],
        },
    };

    private static async Task<(Session Session, FakeTransport Transport, ManifestPanel Panel)> OpenAsync(DeviceManifest manifest, CancellationToken cancellationToken)
    {
        var transport = new FakeTransport();
        var session = new Session(transport, new Pipeline([]));
        await session.OpenAsync(cancellationToken);
        return (session, transport, ManifestPanel.Attach(session, manifest));
    }

    private static string Sent(FakeTransport transport, int index) => Encoding.ASCII.GetString(transport.WrittenPayloads[index]);

    [TestMethod]
    public async Task InvokeAsync_SendsTheFormattedTemplatePlusTheTerminator()
    {
        var (session, transport, panel) = await OpenAsync(BuildPowerSupply(), TestContext.CancellationToken);
        await using var _ = session;
        using var __ = panel;

        await panel.Surface.InvokeAsync("read", null, TestContext.CancellationToken);
        await panel.Surface.InvokeAsync("vset", "12.5", TestContext.CancellationToken);
        await panel.Surface.InvokeAsync("vset", "99", TestContext.CancellationToken);
        await panel.Surface.InvokeAsync("vset", "", TestContext.CancellationToken);
        await panel.Surface.InvokeAsync("ch", "2.6,HIGH", TestContext.CancellationToken);
        await panel.Surface.InvokeAsync("ch", "9", TestContext.CancellationToken);
        await panel.Surface.InvokeAsync("out", "1", TestContext.CancellationToken);

        Assert.AreEqual("READ?\n", Sent(transport, 0));
        Assert.AreEqual("VSET1:12.50\n", Sent(transport, 1), "Format applied.");
        Assert.AreEqual("VSET1:30.00\n", Sent(transport, 2), "Clamped to the parameter's maximum.");
        Assert.AreEqual("VSET1:05.00\n", Sent(transport, 3), "No value: the parameter's default.");
        Assert.AreEqual("CH3:HIGH\n", Sent(transport, 4), "Comma-joined values fill the parameters in order; an integer rounds.");
        Assert.AreEqual("CH4:LOW\n", Sent(transport, 5), "A missing second value falls back to its default.");
        Assert.AreEqual("OUT1\n", Sent(transport, 6), "A parameterless template's {value} takes the control's value.");
    }

    [TestMethod]
    public async Task PreviewCommand_MatchesWhatWouldBeSent_WithControlCharactersEscaped()
    {
        var (session, transport, panel) = await OpenAsync(BuildPowerSupply(), TestContext.CancellationToken);
        await using var _ = session;
        using var __ = panel;

        Assert.AreEqual("VSET1:07.25\\n", panel.Surface.PreviewCommand("vset", "7.25"));
        Assert.AreEqual("READ?\\n", panel.Surface.PreviewCommand("read", null));
        Assert.IsNull(panel.Surface.PreviewCommand("vset.volts", "3"), "A parameter field sends nothing itself.");
        Assert.IsNull(panel.Surface.PreviewCommand("levels", null), "A display control sends nothing.");
        Assert.IsNull(panel.Surface.PreviewCommand("nope", null));
        Assert.IsEmpty(transport.WrittenPayloads, "A preview never sends.");
    }

    [TestMethod]
    public async Task InvokeAsync_PassiveIdsAreNoOps_AndAnUnknownCommandThrows()
    {
        var (session, transport, panel) = await OpenAsync(BuildPowerSupply(), TestContext.CancellationToken);
        await using var _ = session;
        using var __ = panel;

        await panel.Surface.InvokeAsync("vset.volts", "3", TestContext.CancellationToken);
        await panel.Surface.InvokeAsync("read.reply", null, TestContext.CancellationToken);
        Assert.IsEmpty(transport.WrittenPayloads);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => panel.Surface.InvokeAsync("nope", null, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task QueryReply_IsCorrelated_AndResponsePatternsPublishNamedGroups()
    {
        var (session, transport, panel) = await OpenAsync(BuildPowerSupply(), TestContext.CancellationToken);
        await using var _ = session;
        using var __ = panel;

        var published = new ConcurrentQueue<IReadOnlyDictionary<string, string>>();
        panel.Presenter.ValuesChanged += (_, values) => published.Enqueue(values);

        await panel.Surface.InvokeAsync("read", null, TestContext.CancellationToken);
        await transport.PushIncomingAsync("V=12.00 I=0.50\r\n"u8.ToArray());
        await transport.PushIncomingAsync("V=11.90 I=0.52\n"u8.ToArray());

        Assert.IsTrue(SpinWait.SpinUntil(() => published.Count >= 2, _timeout));
        var values = published.ToArray();

        Assert.AreEqual("V=12.00 I=0.50", values[0]["read.reply"], "The query's reply lands in its reply indicator.");
        Assert.AreEqual("12.00", values[0]["volts"]);
        Assert.AreEqual("0.50", values[0]["amps"]);
        Assert.AreEqual("12.00", values[0]["reading"], "The pattern's own name gets the first group.");

        Assert.IsFalse(values[1].ContainsKey("read.reply"), "An unsolicited line isn't a reply to anything...");
        Assert.AreEqual("11.90", values[1]["volts"], "...but its pattern captures still publish (streamed telemetry).");
    }

    [TestMethod]
    public async Task Presenter_RendersNoText_AndIsUnboundWhenThePanelCloses()
    {
        var (session, _, panel) = await OpenAsync(BuildPowerSupply(), TestContext.CancellationToken);
        await using var __ = session;

        Assert.Contains(panel.Presenter, session.Presenters);
        Assert.IsEmpty(panel.Presenter.Render(new System.Buffers.ReadOnlySequence<byte>("V=1 I=2\n"u8.ToArray())), "The connection's own presenters already show every line.");

        panel.Dispose();
        panel.Dispose();

        Assert.DoesNotContain(panel.Presenter, session.Presenters);
    }

    [TestMethod]
    public void UiBuilder_WithNoUi_GeneratesAButtonPerCommand_ParameterFields_AndReplyIndicators()
    {
        var manifest = BuildPowerSupply();
        manifest.Ui = null;
        manifest.Description = "Bench supply notes.";

        var definition = ManifestUiBuilder.Build(manifest);
        var controls = definition.Sections.Single().Controls;

        Assert.AreEqual("Bench supply notes.", definition.Description);
        var field = (TextFieldControl)controls.Single(c => c.Id == "vset.volts");
        Assert.AreEqual(ValueKind.Number, field.Constraint!.Kind);
        Assert.AreEqual(30, field.Constraint.Maximum);
        var button = (ButtonControl)controls.Single(c => c.Id == "vset.send");
        Assert.AreEqual("vset", button.CommandId);
        Assert.AreSequenceEqual(new[] { "vset.volts" }, button.ParameterFieldIds!.ToArray());
        Assert.IsInstanceOfType<IndicatorControl>(controls.Single(c => c.Id == "read.reply"));
        Assert.IsNull(((ButtonControl)controls.Single(c => c.Id == "read.send")).ParameterFieldIds);
    }

    [TestMethod]
    public void Catalog_FindsFoldersJsonFilesAndZips_UsingEachManifestsOwnName()
    {
        var root = Path.Combine(Path.GetTempPath(), "devterm-manifest-catalog", Path.GetRandomFileName());
        var folder = Path.Combine(root, "supply");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, DeviceManifestLoader.ManifestFileName), DeviceManifestSerializer.ToJson(BuildPowerSupply()));
            File.WriteAllText(Path.Combine(root, "single.json"), DeviceManifestSerializer.ToJson(new DeviceManifest { Name = "Single File" }));
            File.WriteAllText(Path.Combine(root, "broken.json"), "{ not json");
            ZipFile.CreateFromDirectory(folder, Path.Combine(root, "packed.zip"));
            Directory.CreateDirectory(Path.Combine(root, "not-a-manifest"));

            var entries = ManifestCatalog.Discover((root, "user"), (Path.Combine(root, "missing"), "installed"));

            Assert.AreSequenceEqual(
                new[] { "Test Supply (user)", "broken (user)", "packed (user)", "Single File (user)" },
                entries.Select(e => e.DisplayName).ToArray());
            Assert.AreEqual(folder, entries[0].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task BundledLoopbackDemo_EndToEnd_OverTheRealLoopbackTransport()
    {
        // The example manifest ships next to the app ("manifests\{device}\device.json", where
        // DevTermUserDataPaths.AppManifestsDirectory looks) — loaded from there, as the picker would.
        var manifestFolder = Path.Combine(AppContext.BaseDirectory, "manifests", "loopback-sensor-demo");
        var manifest = DeviceManifestLoader.Load(manifestFolder);
        Assert.AreEqual("Loopback Sensor Demo", manifest.Name);

        var transport = new LoopbackTransport(Options.Create(new LoopbackTransportOptions()));
        await using var session = new Session(transport, new Pipeline([]));
        await session.OpenAsync(TestContext.CancellationToken);
        using var panel = ManifestPanel.Attach(session, manifest);

        var published = new ConcurrentQueue<IReadOnlyDictionary<string, string>>();
        panel.Presenter.ValuesChanged += (_, values) => published.Enqueue(values);

        await panel.Surface.InvokeAsync("measure", null, TestContext.CancellationToken);
        Assert.IsTrue(SpinWait.SpinUntil(() => !published.IsEmpty, _timeout));
        published.TryDequeue(out var first);
        Assert.AreEqual(LoopbackGenerators.SensorSample(0), first!["measure.reply"]);
        Assert.AreEqual("50.00", first["chA"]);
        Assert.AreEqual("90.00", first["chB"]);
        Assert.AreEqual("0.80", first["x"]);

        Assert.AreEqual("Samples: 5\\n", panel.Surface.PreviewCommand("samples", "5"));
        await panel.Surface.InvokeAsync("samples", "5", TestContext.CancellationToken);
        Assert.IsTrue(SpinWait.SpinUntil(() => published.Count >= 5, _timeout), "Each streamed sample line publishes its own values.");
        Assert.IsTrue(published.All(v => v.ContainsKey("chA") && v.ContainsKey("theta") && !v.ContainsKey("measure.reply")));

        // Every display control in the bundled panel reads ids the pattern actually publishes.
        var publishedIds = first.Keys.ToHashSet();
        foreach (var display in panel.Definition.Sections.SelectMany(s => s.Controls).Select(LiveDisplayState.For).OfType<LiveDisplayState>())
        {
            foreach (var id in display.ValueIds)
            {
                Assert.Contains(id, publishedIds);
            }
        }
    }
}
