using DevTerm.Core.Routing;

namespace DevTerm.Configuration;

/// <summary>
/// The front-end-neutral state behind the Routing window (docs/specs/routing-window.md): an editable draft of the tab's routing
/// section, the sample-line test, Apply/Save, and a read-out of the running service. WPF and the TUI both drive this and only
/// differ in widgets.
/// </summary>
public sealed class RoutingViewModel
{
    private readonly SessionTab _tab;
    private readonly ConnectionProfileStore? _store;

    public RoutingViewModel(SessionTab tab, ConnectionProfileStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tab = tab;
        _store = store;
        Draft = Clone(tab.CliOptions.Routing);
    }

    /// <summary>The routing section being edited; Apply hands a copy of it to the tab.</summary>
    public RoutingOptions Draft { get; private set; }

    public static IReadOnlyList<string> Protocols { get; } = ["mqtt", "amqp", "stomp"];

    public RoutingState State => _tab.Routing.State;

    /// <summary>The Routing window's and status line's one-line state: "Broker: Connected", "Broker: Reconnecting (reason)", ...</summary>
    public string StatusText => State switch
    {
        RoutingState.Stopped => "Broker: Stopped",
        RoutingState.Connecting => "Broker: Connecting",
        RoutingState.Connected => "Broker: Connected",
        RoutingState.Reconnecting => $"Broker: Reconnecting ({_tab.Routing.Reason})",
        _ => $"Broker: Failed ({_tab.Routing.Reason})",
    };

    public int Unmatched => _tab.Routing.Router?.Unmatched ?? 0;

    /// <summary>Messages dropped: device lines with no broker connection, plus confirm-flagged sends the user dropped.</summary>
    public int Dropped => _tab.Routing.DroppedPublishes + (_tab.Routing.Router?.Dropped ?? 0);

    public IReadOnlyList<RoutedMessage> History => _tab.Routing.Router?.History ?? [];

    /// <summary>The running router's hit count for a draft rule's applied twin (by position); 0 when stopped or the rule is not applied yet.</summary>
    public int Hits(int ruleIndex)
    {
        var applied = _tab.CliOptions.Routing.Rules;
        return _tab.Routing.Router is { } router && ruleIndex < applied.Count ? router.HitCount(applied[ruleIndex]) : 0;
    }

    public void AddRule() => Draft.Rules.Add(new RoutingRule { Direction = RoutingDirection.DeviceToBroker });

    public void RemoveRule(int index)
    {
        if (index >= 0 && index < Draft.Rules.Count)
        {
            Draft.Rules.RemoveAt(index);
        }
    }

    public void MoveRule(int index, int delta)
    {
        var to = index + delta;
        if (index < 0 || index >= Draft.Rules.Count || to < 0 || to >= Draft.Rules.Count)
        {
            return;
        }

        (Draft.Rules[index], Draft.Rules[to]) = (Draft.Rules[to], Draft.Rules[index]);
    }

    /// <summary>Runs <paramref name="sample"/> through every draft rule, no side effects: one line per rule saying what it would do.</summary>
    public IReadOnlyList<string> Test(string sample)
    {
        var lines = new List<string>();
        for (var i = 0; i < Draft.Rules.Count; i++)
        {
            var rule = Draft.Rules[i];
            if (rule.Validate() is { } problem)
            {
                lines.Add($"Rule {i + 1}: invalid - {problem}");
            }
            else if (rule.Test(sample) is { } result)
            {
                lines.Add(rule.Direction == RoutingDirection.DeviceToBroker
                    ? $"Rule {i + 1}: matches -> topic {result.Topic}, payload {result.Text}"
                    : $"Rule {i + 1}: matches -> send {result.Text} (from {result.Topic})");
            }
            else
            {
                lines.Add($"Rule {i + 1}: no match");
            }
        }

        return lines;
    }

    /// <summary>The first problem with the draft, or null when it can be applied.</summary>
    public string? Validate() => Draft.Validate();

    /// <summary>Applies the draft to the tab: stops the old routing, and starts the new one when the session is open. Returns the problem, or null on success.</summary>
    public async Task<string?> ApplyAsync()
    {
        if (Validate() is { } problem)
        {
            return problem;
        }

        await _tab.ApplyRoutingAsync(Clone(Draft)).ConfigureAwait(false);
        return null;
    }

    /// <summary>Starts routing on the open session using the applied rules (the window's Start). Returns the problem, or null.</summary>
    public string? Start()
    {
        if (_tab.Session.State != Core.Transports.ConnectionState.Open)
        {
            return "Connect the device first.";
        }

        if (!_tab.HasRouting)
        {
            return "Add a broker host and at least one rule, then Apply.";
        }

        _tab.StartRouting();
        return _tab.Routing.State == RoutingState.Failed ? _tab.Routing.Reason : null;
    }

    public Task StopAsync() => _tab.Routing.StopAsync();

    /// <summary>Saves the applied routing section into the saved profile this connection came from. Returns a message saying what happened.</summary>
    public string SaveToProfile()
    {
        if (_store is null)
        {
            return "No profile store: the change lives only in this session.";
        }

        var applied = _tab.CliOptions.Routing;
        _tab.CliOptions.Routing = new RoutingOptions();
        string? name;
        try
        {
            name = _store.FindName(_tab.CliOptions);
        }
        finally
        {
            _tab.CliOptions.Routing = applied;
        }

        if (name is null)
        {
            return "This connection is not a saved profile; save it from Device Profiles first.";
        }

        _store.Save(name, _tab.CliOptions);
        return $"Saved to profile '{name}'.";
    }

    /// <summary>Runs <see cref="SessionTab.RoutingConfirm"/>'s decision through a front end's prompt for the tab.</summary>
    public void UseConfirm(Func<RoutingRule, string, Task<RoutingConfirmChoice>> prompt) => _tab.RoutingConfirm = prompt;

    private static RoutingOptions Clone(RoutingOptions source) => new()
    {
        Protocol = source.Protocol,
        Host = source.Host,
        Port = source.Port,
        Username = source.Username,
        Password = source.Password,
        Tls = source.Tls,
        Rules = [.. source.Rules.Select(CloneRule)],
    };

    private static RoutingRule CloneRule(RoutingRule rule) => new()
    {
        Direction = rule.Direction,
        Match = rule.Match,
        Topic = rule.Topic,
        Payload = rule.Payload,
        Send = rule.Send,
        Confirm = rule.Confirm,
    };
}
