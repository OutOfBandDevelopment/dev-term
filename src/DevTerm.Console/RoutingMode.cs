using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using DevTerm.Configuration;
using DevTerm.Core.Routing;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI's Routing window (Device > Routing...; docs/specs/routing-window.md): the broker, the rules, a sample-line test, Apply /
/// Start / Stop / Save, and a live history. All state lives in <see cref="RoutingViewModel"/>; this is only widgets. Every explicit
/// line stays shorter than its label, since a Terminal.Gui label wraps and drops what falls past its height.
/// </summary>
internal static class RoutingMode
{
    internal static RoutingWindowParts BuildWindow(IApplication app, RoutingViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(vm);

        var window = new Window { Title = "dev-term — Routing", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var noHotKey = (Rune)0xFFFF;
        var loading = false;

        var status = new Label { X = 0, Y = 0, Width = Dim.Fill(), HotKeySpecifier = noHotKey };
        var message = new Label { X = 0, Y = 1, Width = Dim.Fill(), HotKeySpecifier = noHotKey };

        var protocol = new Button { X = 0, Y = 2, Text = "mqtt", ShadowStyle = ShadowStyles.None };
        var hostLabel = new Label { X = Pos.Right(protocol) + 1, Y = 2, Text = "Host:" };
        var host = new TextField { X = Pos.Right(hostLabel) + 1, Y = 2, Width = 22 };
        var portLabel = new Label { X = Pos.Right(host) + 1, Y = 2, Text = "Port:" };
        var port = new TextField { X = Pos.Right(portLabel) + 1, Y = 2, Width = 6 };
        var userLabel = new Label { X = 0, Y = 4, Text = "User:" };
        var user = new TextField { X = Pos.Right(userLabel) + 1, Y = 4, Width = 16 };
        var passLabel = new Label { X = Pos.Right(user) + 1, Y = 4, Text = "Password:" };
        var password = new TextField { X = Pos.Right(passLabel) + 1, Y = 4, Width = 16, Secret = true };

        var rulesList = new ListView { X = 0, Y = 5, Width = Dim.Fill(), Height = 3 };
        var ruleRows = new ObservableCollection<string>();
        rulesList.SetSource(ruleRows);

        var add = new Button { X = 0, Y = 8, Text = "Add", ShadowStyle = ShadowStyles.None };
        var remove = new Button { X = Pos.Right(add), Y = 8, Text = "Remove", ShadowStyle = ShadowStyles.None };
        var up = new Button { X = Pos.Right(remove), Y = 8, Text = "Up", ShadowStyle = ShadowStyles.None };
        var down = new Button { X = Pos.Right(up), Y = 8, Text = "Down", ShadowStyle = ShadowStyles.None };

        var direction = new Button { X = 0, Y = 10, Text = "device->broker", ShadowStyle = ShadowStyles.None };
        var confirm = new CheckBox { X = Pos.Right(direction) + 2, Y = 10, Text = "Confirm sends" };
        var matchLabel = new Label { X = 0, Y = 12, Text = "Match:" };
        var match = new TextField { X = 9, Y = 12, Width = Dim.Fill() };
        var topicLabel = new Label { X = 0, Y = 13, Text = "Topic:" };
        var topic = new TextField { X = 9, Y = 13, Width = Dim.Fill() };
        var payloadLabel = new Label { X = 0, Y = 14, Text = "Payload:", Width = 8 };
        var payload = new TextField { X = 9, Y = 14, Width = Dim.Fill() };

        var sampleLabel = new Label { X = 0, Y = 15, Text = "Sample:" };
        var sample = new TextField { X = 9, Y = 15, Width = Dim.Fill(10) };
        var test = new Button { X = Pos.Right(sample), Y = 15, Text = "Test", ShadowStyle = ShadowStyles.None };
        var testResult = new Label { X = 0, Y = 16, Width = Dim.Fill(), Height = 1, HotKeySpecifier = noHotKey };

        var history = new ListView { X = 0, Y = 17, Width = Dim.Fill(), Height = Dim.Fill(4) };
        var historyRows = new ObservableCollection<string>();
        history.SetSource(historyRows);
        var counters = new Label { X = 0, Y = Pos.AnchorEnd(3), Width = Dim.Fill(), HotKeySpecifier = noHotKey };

        var apply = new Button { X = 0, Y = Pos.AnchorEnd(2), Text = "Apply", ShadowStyle = ShadowStyles.None };
        var startStop = new Button { X = Pos.Right(apply), Y = Pos.AnchorEnd(2), Text = "Start", ShadowStyle = ShadowStyles.None };
        var save = new Button { X = Pos.Right(startStop), Y = Pos.AnchorEnd(2), Text = "Save", ShadowStyle = ShadowStyles.None };
        var close = new Button { X = Pos.Right(save), Y = Pos.AnchorEnd(2), Text = "Close", IsDefault = true, ShadowStyle = ShadowStyles.None };

        RoutingRule? Selected() =>
            rulesList.SelectedItem is int i && i >= 0 && i < vm.Draft.Rules.Count ? vm.Draft.Rules[i] : null;

        string RuleLabel(int i)
        {
            var r = vm.Draft.Rules[i];
            var arrow = r.Direction == RoutingDirection.DeviceToBroker ? "->" : "<-";
            var bad = r.Validate() is null ? string.Empty : " (invalid)";
            return $"{i + 1} {arrow} {r.Match} {r.Topic} hits {vm.Hits(i)}{bad}";
        }

        void RebuildRules(int select)
        {
            loading = true;
            ruleRows.Clear();
            for (var i = 0; i < vm.Draft.Rules.Count; i++)
            {
                ruleRows.Add(RuleLabel(i));
            }

            if (vm.Draft.Rules.Count > 0)
            {
                rulesList.SelectedItem = Math.Clamp(select, 0, vm.Draft.Rules.Count - 1);
            }

            loading = false;
            LoadRule();
        }

        void LoadRule()
        {
            loading = true;
            if (Selected() is { } rule)
            {
                var toDevice = rule.Direction == RoutingDirection.BrokerToDevice;
                direction.Text = toDevice ? "broker->device" : "device->broker";
                match.Text = rule.Match;
                topic.Text = rule.Topic;
                payloadLabel.Text = toDevice ? "Send:" : "Payload:";
                payload.Text = (toDevice ? rule.Send : rule.Payload) ?? string.Empty;
                confirm.Value = rule.Confirm ? CheckState.Checked : CheckState.UnChecked;
                confirm.Enabled = toDevice;
            }
            else
            {
                match.Text = topic.Text = payload.Text = string.Empty;
            }

            loading = false;
        }

        void StoreRule()
        {
            if (loading || Selected() is not { } rule)
            {
                return;
            }

            var toDevice = rule.Direction == RoutingDirection.BrokerToDevice;
            rule.Match = match.Text;
            rule.Topic = topic.Text;
            if (toDevice)
            {
                rule.Send = payload.Text;
            }
            else
            {
                rule.Payload = string.IsNullOrEmpty(payload.Text) ? null : payload.Text;
            }

            rule.Confirm = toDevice && confirm.Value == CheckState.Checked;
            var index = (int)rulesList.SelectedItem!;
            loading = true;
            ruleRows[index] = RuleLabel(index);
            loading = false;
        }

        void StoreBroker()
        {
            if (loading)
            {
                return;
            }

            var d = vm.Draft;
            d.Protocol = protocol.Text;
            d.Host = host.Text.Trim();
            d.Port = int.TryParse(port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 0;
            d.Username = string.IsNullOrEmpty(user.Text) ? null : user.Text;
            d.Password = string.IsNullOrEmpty(password.Text) ? null : password.Text;
        }

        void Say(string text) => message.Text = text.ReplaceLineEndings(" ");

        void Refresh()
        {
            status.Text = " " + vm.StatusText;
            startStop.Text = vm.State is RoutingState.Stopped or RoutingState.Failed ? "Start" : "Stop";
            counters.Text = $"Unmatched: {vm.Unmatched}   Dropped: {vm.Dropped}";
            for (var i = 0; i < ruleRows.Count && i < vm.Draft.Rules.Count; i++)
            {
                var label = RuleLabel(i);
                if (ruleRows[i] != label)
                {
                    ruleRows[i] = label;
                }
            }

            var messages = vm.History;
            if (historyRows.Count != messages.Count)
            {
                historyRows.Clear();
                foreach (var m in messages)
                {
                    historyRows.Add($"{m.Timestamp:HH:mm:ss} {(m.Direction == RoutingDirection.DeviceToBroker ? "->" : "<-")} {m.Topic} {m.Payload}");
                }

                if (historyRows.Count > 0)
                {
                    history.SelectedItem = historyRows.Count - 1;
                }
            }
        }

        void RunTest()
        {
            var lines = vm.Test(sample.Text);
            testResult.Text = lines.Count == 0 ? "No rules to test." : string.Join("; ", lines);
        }

        async Task ApplyAsync()
        {
            StoreBroker();
            var problem = await vm.ApplyAsync();
            Say(problem ?? (vm.Draft.IsConfigured ? "Applied. Routing restarts with the new rules." : "Applied (no broker host or rules, so routing stays off)."));
            Refresh();
        }

        void Wire(Button button, Action action) => button.Accepting += (_, e) =>
        {
            e.Handled = true;
            action();
        };

        Wire(protocol, () =>
        {
            var all = RoutingViewModel.Protocols;
            protocol.Text = all[(all.ToList().IndexOf(protocol.Text) + 1) % all.Count];
            StoreBroker();
        });
        Wire(add, () =>
        {
            vm.AddRule();
            RebuildRules(vm.Draft.Rules.Count - 1);
        });
        Wire(remove, () =>
        {
            var at = rulesList.SelectedItem as int? ?? -1;
            vm.RemoveRule(at);
            RebuildRules(at);
        });
        Wire(up, () => Move(-1));
        Wire(down, () => Move(1));
        void Move(int delta)
        {
            var at = rulesList.SelectedItem as int? ?? -1;
            vm.MoveRule(at, delta);
            RebuildRules(at + delta);
        }

        Wire(direction, () =>
        {
            if (Selected() is { } rule)
            {
                rule.Direction = rule.Direction == RoutingDirection.DeviceToBroker ? RoutingDirection.BrokerToDevice : RoutingDirection.DeviceToBroker;
                LoadRule();
                StoreRule();
            }
        });
        Wire(test, RunTest);
        Wire(apply, () => _ = ObserveAsync(ApplyAsync()));
        Wire(startStop, () => _ = ObserveAsync(StartStopAsync()));
        Wire(save, () => Say(vm.SaveToProfile()));
        Wire(close, () => app.RequestStop());

        async Task StartStopAsync()
        {
            if (vm.State is RoutingState.Stopped or RoutingState.Failed)
            {
                Say(vm.Start() ?? string.Empty);
            }
            else
            {
                await vm.StopAsync();
                Say("Routing stopped. It starts again the next time the device connects.");
            }

            Refresh();
        }

        async Task ObserveAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Say(ex.Message);
            }
        }

        rulesList.ValueChanged += (_, _) => LoadRule();
        match.TextChanged += (_, _) => StoreRule();
        topic.TextChanged += (_, _) => StoreRule();
        payload.TextChanged += (_, _) => StoreRule();
        confirm.ValueChanged += (_, _) => StoreRule();
        host.TextChanged += (_, _) => StoreBroker();
        port.TextChanged += (_, _) => StoreBroker();
        user.TextChanged += (_, _) => StoreBroker();
        password.TextChanged += (_, _) => StoreBroker();

        loading = true;
        protocol.Text = vm.Draft.Protocol.ToLowerInvariant();
        host.Text = vm.Draft.Host;
        port.Text = vm.Draft.Port == 0 ? string.Empty : vm.Draft.Port.ToString(CultureInfo.InvariantCulture);
        user.Text = vm.Draft.Username ?? string.Empty;
        password.Text = vm.Draft.Password ?? string.Empty;
        loading = false;
        RebuildRules(0);
        Refresh();

        window.Add(
            status, message, protocol, hostLabel, host, portLabel, port, userLabel, user, passLabel, password,
            rulesList, add, remove, up, down, direction, confirm, matchLabel, match, topicLabel, topic, payloadLabel, payload,
            sampleLabel, sample, test, testResult, history, counters, apply, startStop, save, close);

        // Polls the service so the state, counters and history stay live while the dialog is open.
        var timer = app.AddTimeout(TimeSpan.FromMilliseconds(500), () =>
        {
            Refresh();
            return true;
        });
        window.Disposing += (_, _) =>
        {
            if (timer is not null)
            {
                app.RemoveTimeout(timer);
            }
        };

        return new RoutingWindowParts(window, status, message, protocol, host, rulesList, match, topic, payload, sample, testResult, history, apply, startStop, close, Refresh, RunTest, ApplyAsync);
    }

    /// <summary>The prompt for a confirm-flagged broker message, shown on the UI thread: Send once / Always this session / Drop.</summary>
    internal static Task<RoutingConfirmChoice> ConfirmAsync(IApplication app, RoutingRule rule, string text)
    {
        var done = new TaskCompletionSource<RoutingConfirmChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Invoke(() =>
        {
            var answer = MessageBox.Query(app, "dev-term — routing", $"Broker message on '{rule.Topic}' routes to the device:\n{text}", ["Send once", "Always this session", "Drop"]);
            done.TrySetResult(answer switch
            {
                0 => RoutingConfirmChoice.SendOnce,
                1 => RoutingConfirmChoice.Always,
                _ => RoutingConfirmChoice.Drop,
            });
        });
        return done.Task;
    }
}

internal sealed record RoutingWindowParts(
    Window Window,
    Label Status,
    Label Message,
    Button Protocol,
    TextField Host,
    ListView Rules,
    TextField Match,
    TextField Topic,
    TextField Payload,
    TextField Sample,
    Label TestResult,
    ListView History,
    Button Apply,
    Button StartStop,
    Button Close,
    Action Refresh,
    Action RunTest,
    Func<Task> ApplyAsync);
