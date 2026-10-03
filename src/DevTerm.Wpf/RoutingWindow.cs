using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DevTerm.Configuration;
using DevTerm.Core.Routing;

namespace DevTerm.Wpf;

/// <summary>
/// The Routing window (docs/specs/routing-window.md): edits the tab's broker and rules, tests a sample line against them, starts and
/// stops routing, and shows what flowed. All state lives in <see cref="RoutingViewModel"/>; this is only widgets.
/// </summary>
internal sealed class RoutingWindow : Window
{
    private readonly RoutingViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private readonly TextBlock _status = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _protocol = new() { Width = 80 };
    private readonly TextBox _host = new() { Width = 160 };
    private readonly TextBox _port = new() { Width = 60 };
    private readonly TextBox _user = new() { Width = 100 };
    private readonly PasswordBox _password = new() { Width = 100 };
    private readonly ListBox _rules = new() { MinHeight = 90, MaxHeight = 140 };
    private readonly ComboBox _direction = new() { Width = 130 };
    private readonly TextBox _match = new();
    private readonly TextBox _topic = new();
    private readonly TextBox _payload = new();
    private readonly TextBlock _payloadLabel = new() { Text = "Payload" };
    private readonly CheckBox _confirm = new() { Content = "Confirm before sending to the device" };
    private readonly TextBox _sample = new();
    private readonly TextBox _testResult = new() { IsReadOnly = true, MinLines = 2, FontFamily = new FontFamily("Consolas") };
    private readonly ListBox _history = new() { FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock _counters = new();
    private readonly Button _startStop = new() { Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(8, 0, 0, 0) };
    private bool _loading;

    public RoutingWindow(RoutingViewModel vm)
    {
        _vm = vm;
        Title = "dev-term - Routing";
        Width = 760;
        Height = 700;
        MinWidth = 600;
        MinHeight = 520;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = Build();
        WpfTheme.Attach(this);

        _protocol.ItemsSource = RoutingViewModel.Protocols;
        _direction.ItemsSource = Enum.GetValues<RoutingDirection>();
        LoadBroker();
        RefreshRules(0);
        Refresh();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    internal string StatusText => _status.Text;

    internal string MessageText => _message.Text;

    internal string TestResultText => _testResult.Text;

    internal int RuleCount => _rules.Items.Count;

    internal void SetHost(string host) => _host.Text = host;

    internal void SetSample(string sample) => _sample.Text = sample;

    internal void SelectRule(int index) => _rules.SelectedIndex = index;

    internal void RunTest() => Test_Click(this, new RoutedEventArgs());

    internal void AddRule() => AddRule_Click(this, new RoutedEventArgs());

    internal void SetRule(string match, string topic)
    {
        _match.Text = match;
        _topic.Text = topic;
    }

    internal async Task ApplyForTestAsync() => await ApplyAsync();

    /// <summary>The three-way prompt for a confirm-flagged broker message (Send once / Always this session / Drop).</summary>
    internal static Task<RoutingConfirmChoice> ConfirmAsync(Window? owner, RoutingRule rule, string text)
    {
        var choice = RoutingConfirmChoice.Drop;
        var dialog = new Window
        {
            Title = "dev-term - Routing: confirm send",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 520,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }

        Button Make(string label, RoutingConfirmChoice value, bool isDefault = false)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 8, 0), IsDefault = isDefault };
            button.Click += (_, _) =>
            {
                choice = value;
                dialog.Close();
            };
            return button;
        }

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = $"The broker sent a message on '{rule.Topic}' that routes to the device:", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, Margin = new Thickness(0, 8, 0, 8), FontFamily = new FontFamily("Consolas") });
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Make("Send once", RoutingConfirmChoice.SendOnce, true), Make("Always this session", RoutingConfirmChoice.Always), Make("Drop", RoutingConfirmChoice.Drop) },
        });
        dialog.Content = panel;
        WpfTheme.Attach(dialog);
        dialog.ShowDialog();
        return Task.FromResult(choice);
    }

    private UIElement Build()
    {
        var root = new StackPanel { Margin = new Thickness(10) };

        _startStop.Click += (_, _) => Observe(StartStopAsync());
        var apply = new Button { Content = "Apply", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(8, 0, 0, 0) };
        apply.Click += (_, _) => Observe(ApplyAsync());
        var save = new Button { Content = "Save to profile", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(8, 0, 0, 0) };
        save.Click += (_, _) => Say(_vm.SaveToProfile());
        var top = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { apply, save, _startStop } };
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(_status);
        root.Children.Add(top);
        root.Children.Add(_message);

        root.Children.Add(Heading("Broker"));
        root.Children.Add(Row(("Protocol", _protocol), ("Host", _host), ("Port", _port), ("User", _user), ("Password", _password)));
        _host.ToolTip = "Broker host name or address";
        _port.ToolTip = "Blank uses the protocol's default port";
        _password.ToolTip = "Saved in the profile as plain text; DEVTERM_ROUTING__PASSWORD overrides it";
        _protocol.SelectionChanged += (_, _) => Store();
        foreach (var box in new[] { _host, _port, _user })
        {
            box.TextChanged += (_, _) => Store();
        }

        _password.PasswordChanged += (_, _) => Store();

        root.Children.Add(Heading("Rules (the first matching device rule wins)"));
        _rules.SelectionChanged += (_, _) => LoadRule();
        root.Children.Add(_rules);
        var ruleButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var (label, handler) in new (string, RoutedEventHandler)[] { ("Add", AddRule_Click), ("Remove", RemoveRule_Click), ("Up", (_, _) => Move(-1)), ("Down", (_, _) => Move(1)) })
        {
            var b = new Button { Content = label, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
            b.Click += handler;
            ruleButtons.Children.Add(b);
        }

        root.Children.Add(ruleButtons);
        root.Children.Add(Row(("Direction", _direction)));
        _match.ToolTip = "Regex; named groups feed ${name} in the topic and payload";
        _topic.ToolTip = "Broker topic or address; ${name} allowed for device-to-broker";
        root.Children.Add(Field("Match", _match));
        root.Children.Add(Field("Topic", _topic));
        var payloadRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        _payloadLabel.Width = 60;
        _payloadLabel.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_payloadLabel, Dock.Left);
        payloadRow.Children.Add(_payloadLabel);
        payloadRow.Children.Add(_payload);
        root.Children.Add(payloadRow);
        _confirm.Margin = new Thickness(60, 2, 0, 2);
        root.Children.Add(_confirm);
        _direction.SelectionChanged += (_, _) => StoreRule();
        foreach (var box in new[] { _match, _topic, _payload })
        {
            box.TextChanged += (_, _) => StoreRule();
        }

        _confirm.Click += (_, _) => StoreRule();

        root.Children.Add(Heading("Test a sample line"));
        var test = new Button { Content = "Test", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(8, 0, 0, 0) };
        test.Click += Test_Click;
        var testRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        DockPanel.SetDock(test, Dock.Right);
        testRow.Children.Add(test);
        testRow.Children.Add(_sample);
        root.Children.Add(testRow);
        root.Children.Add(_testResult);

        root.Children.Add(Heading("History"));
        _history.Height = 130;
        root.Children.Add(_history);
        root.Children.Add(_counters);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };

    private static StackPanel Row(params (string Label, FrameworkElement Control)[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var (label, control) in items)
        {
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            control.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(control);
        }

        return row;
    }

    private static DockPanel Field(string label, TextBox box)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var text = new TextBlock { Text = label, Width = 60, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(box);
        return row;
    }

    private void LoadBroker()
    {
        _loading = true;
        var d = _vm.Draft;
        _protocol.SelectedItem = d.Protocol.ToLowerInvariant();
        _host.Text = d.Host;
        _port.Text = d.Port == 0 ? string.Empty : d.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _user.Text = d.Username ?? string.Empty;
        _password.Password = d.Password ?? string.Empty;
        _loading = false;
    }

    private void Store()
    {
        if (_loading)
        {
            return;
        }

        var d = _vm.Draft;
        d.Protocol = _protocol.SelectedItem as string ?? "mqtt";
        d.Host = _host.Text.Trim();
        d.Port = int.TryParse(_port.Text, out var port) ? port : 0;
        d.Username = string.IsNullOrEmpty(_user.Text) ? null : _user.Text;
        d.Password = string.IsNullOrEmpty(_password.Password) ? null : _password.Password;
    }

    private void RefreshRules(int select)
    {
        _loading = true;
        _rules.Items.Clear();
        for (var i = 0; i < _vm.Draft.Rules.Count; i++)
        {
            _rules.Items.Add(RuleLabel(i));
        }

        _rules.SelectedIndex = _vm.Draft.Rules.Count == 0 ? -1 : Math.Clamp(select, 0, _vm.Draft.Rules.Count - 1);
        _loading = false;
        LoadRule();
    }

    private string RuleLabel(int i)
    {
        var r = _vm.Draft.Rules[i];
        var arrow = r.Direction == RoutingDirection.DeviceToBroker ? "->" : "<-";
        var warn = r.Validate() is null ? string.Empty : "  (invalid)";
        return $"{i + 1}  {arrow}  {r.Match}  {r.Topic}  Hits: {_vm.Hits(i)}{warn}";
    }

    private RoutingRule? Selected => _rules.SelectedIndex >= 0 && _rules.SelectedIndex < _vm.Draft.Rules.Count ? _vm.Draft.Rules[_rules.SelectedIndex] : null;

    private void LoadRule()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        var rule = Selected;
        foreach (var control in new UIElement[] { _direction, _match, _topic, _payload, _confirm })
        {
            control.IsEnabled = rule is not null;
        }

        if (rule is not null)
        {
            _direction.SelectedItem = rule.Direction;
            _match.Text = rule.Match;
            _topic.Text = rule.Topic;
            var toDevice = rule.Direction == RoutingDirection.BrokerToDevice;
            _payloadLabel.Text = toDevice ? "Send" : "Payload";
            _payload.Text = (toDevice ? rule.Send : rule.Payload) ?? string.Empty;
            _confirm.IsEnabled = toDevice;
            _confirm.IsChecked = rule.Confirm;
        }

        _loading = false;
    }

    private void StoreRule()
    {
        if (_loading || Selected is not { } rule)
        {
            return;
        }

        rule.Direction = _direction.SelectedItem is RoutingDirection d ? d : RoutingDirection.DeviceToBroker;
        rule.Match = _match.Text;
        rule.Topic = _topic.Text;
        var toDevice = rule.Direction == RoutingDirection.BrokerToDevice;
        _payloadLabel.Text = toDevice ? "Send" : "Payload";
        if (toDevice)
        {
            rule.Send = _payload.Text;
        }
        else
        {
            rule.Payload = string.IsNullOrEmpty(_payload.Text) ? null : _payload.Text;
        }

        rule.Confirm = toDevice && _confirm.IsChecked == true;
        _confirm.IsEnabled = toDevice;
        var index = _rules.SelectedIndex;
        _loading = true;
        _rules.Items[index] = RuleLabel(index);
        _rules.SelectedIndex = index;
        _loading = false;
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        _vm.AddRule();
        RefreshRules(_vm.Draft.Rules.Count - 1);
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        var at = _rules.SelectedIndex;
        _vm.RemoveRule(at);
        RefreshRules(at);
    }

    private void Move(int delta)
    {
        var at = _rules.SelectedIndex;
        _vm.MoveRule(at, delta);
        RefreshRules(at + delta);
    }

    private void Test_Click(object sender, RoutedEventArgs e) => _testResult.Text = string.Join(Environment.NewLine, _vm.Test(_sample.Text));

    private async Task ApplyAsync()
    {
        Store();
        var problem = await _vm.ApplyAsync();
        Say(problem ?? (_vm.Draft.IsConfigured ? "Applied. Routing restarts with the new rules." : "Applied (no broker host or rules, so routing stays off)."));
        Refresh();
    }

    private async Task StartStopAsync()
    {
        if (_vm.State is RoutingState.Stopped or RoutingState.Failed)
        {
            Say(_vm.Start() ?? string.Empty);
        }
        else
        {
            await _vm.StopAsync();
            Say("Routing stopped. It starts again the next time the device connects.");
        }

        Refresh();
    }

    private void Say(string text) => _message.Text = text;

    private void Refresh()
    {
        _status.Text = _vm.StatusText;
        _startStop.Content = _vm.State is RoutingState.Stopped or RoutingState.Failed ? "Start" : "Stop";
        for (var i = 0; i < _rules.Items.Count && i < _vm.Draft.Rules.Count; i++)
        {
            var label = RuleLabel(i);
            if (!Equals(_rules.Items[i], label))
            {
                _rules.Items[i] = label;
            }
        }

        if (_rules.SelectedIndex < 0 && _rules.Items.Count > 0)
        {
            _loading = true;
            _rules.SelectedIndex = 0;
            _loading = false;
        }

        var history = _vm.History;
        if (_history.Items.Count != history.Count)
        {
            _history.Items.Clear();
            foreach (var m in history)
            {
                var arrow = m.Direction == RoutingDirection.DeviceToBroker ? "->" : "<-";
                _history.Items.Add($"{m.Timestamp:HH:mm:ss.fff}  {arrow}  {m.Topic}  {m.Payload}");
            }

            if (_history.Items.Count > 0)
            {
                _history.ScrollIntoView(_history.Items[^1]);
            }
        }

        _counters.Text = $"Unmatched: {_vm.Unmatched}   Dropped: {_vm.Dropped}";
    }

    private void Observe(Task task) =>
        task.ContinueWith(
            t => Dispatcher.BeginInvoke(() => Say(t.Exception?.GetBaseException().Message ?? string.Empty)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
