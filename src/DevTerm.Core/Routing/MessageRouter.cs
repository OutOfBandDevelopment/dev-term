using System.Buffers;
using System.Diagnostics;
using System.Text;
using DevTerm.Core.Presenters;

namespace DevTerm.Core.Routing;

/// <summary>
/// A routing proxy between one device session and a broker. As an <see cref="IOriginatingPresenter"/> it is
/// added to a live <c>Session</c>: each device line matching a device-to-broker rule is published to the
/// <see cref="IMessageSink"/>, and <see cref="OnBrokerMessage"/> turns a matching broker message into bytes the
/// session sends to the device (<see cref="Originated"/>). It renders nothing itself. Every routed message is
/// recorded in <see cref="History"/> with one shared timestamp source. A proof of concept: see
/// docs/design/proposals/message-broker-protocols.md.
/// </summary>
public sealed class MessageRouter : IOriginatingPresenter
{
    private readonly RoutingRuleSet _rules;
    private readonly IMessageSink _sink;
    private readonly TimeProvider _time;
    private readonly string _terminator;
    private readonly StringBuilder _line = new();
    private readonly List<RoutedMessage> _history = [];
    private readonly Lock _gate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<RoutingRule, Counter> _hits = new();
    private readonly HashSet<RoutingRule> _always = [];
    private int _unmatched;
    private int _dropped;

    private sealed class Counter
    {
        public int Value;
    }

    public MessageRouter(RoutingRuleSet rules, IMessageSink sink, TimeProvider? time = null, string terminator = "\r\n")
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(sink);
        _rules = rules;
        _sink = sink;
        _time = time ?? TimeProvider.System;
        _terminator = terminator;
    }

    /// <summary>Asked before the first message of a <see cref="RoutingRule.Confirm"/> rule reaches the device. With none set such a message is dropped, never sent unasked.</summary>
    public Func<RoutingRule, string, Task<RoutingConfirmChoice>>? Confirm { get; set; }

    /// <summary>Raised after a message is routed, dropped or counted as unmatched, so a window can refresh.</summary>
    public event EventHandler? Changed;

    /// <summary>Device lines that matched no device-to-broker rule.</summary>
    public int Unmatched => Volatile.Read(ref _unmatched);

    /// <summary>Broker messages a confirm-flagged rule declined to send.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>How many messages <paramref name="rule"/> has matched.</summary>
    public int HitCount(RoutingRule rule) => _hits.TryGetValue(rule, out var n) ? Volatile.Read(ref n.Value) : 0;

    public string Name => "router";

    public event EventHandler<ReadOnlyMemory<byte>>? Originated;

    /// <summary>Every message routed so far, in order, both directions.</summary>
    public IReadOnlyList<RoutedMessage> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _history];
            }
        }
    }

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        foreach (var ch in Encoding.ASCII.GetString(data.ToArray()))
        {
            if (ch == '\n')
            {
                var text = _line.ToString();
                _line.Clear();
                RouteDeviceLine(text);
            }
            else if (ch != '\r')
            {
                _line.Append(ch);
            }
        }

        return [];
    }

    /// <summary>A message arrived from the broker; sends the first matching rule's text to the device.</summary>
    public void OnBrokerMessage(string topic, string payload)
    {
        foreach (var rule in _rules.Rules)
        {
            if (rule.Direction != RoutingDirection.BrokerToDevice || !rule.TopicMatches(topic))
            {
                continue;
            }

            var match = rule.Compiled.Match(payload);
            if (!match.Success || rule.Send is null)
            {
                continue;
            }

            var text = RoutingRule.Expand(rule.Send, match);
            Hit(rule);
            if (!rule.Confirm || IsAlways(rule))
            {
                Deliver(topic, payload, text);
            }
            else if (Confirm is null)
            {
                Drop();
            }
            else
            {
                _ = ConfirmThenDeliverAsync(rule, topic, payload, text);
            }

            return;
        }
    }

    private bool IsAlways(RoutingRule rule)
    {
        lock (_gate)
        {
            return _always.Contains(rule);
        }
    }

    private async Task ConfirmThenDeliverAsync(RoutingRule rule, string topic, string payload, string text)
    {
        RoutingConfirmChoice choice;
        try
        {
            choice = await Confirm!(rule, text).ConfigureAwait(false);
        }
        catch (Exception)
        {
            choice = RoutingConfirmChoice.Drop;
        }

        if (choice == RoutingConfirmChoice.Drop)
        {
            Drop();
            return;
        }

        if (choice == RoutingConfirmChoice.Always)
        {
            lock (_gate)
            {
                _always.Add(rule);
            }
        }

        Deliver(topic, payload, text);
    }

    private void Deliver(string topic, string payload, string text)
    {
        Record(RoutingDirection.BrokerToDevice, topic, payload);
        Originated?.Invoke(this, Encoding.ASCII.GetBytes(text + _terminator));
    }

    private void Drop()
    {
        Interlocked.Increment(ref _dropped);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Hit(RoutingRule rule) => Interlocked.Increment(ref _hits.GetOrAdd(rule, _ => new Counter()).Value);

    private void RouteDeviceLine(string line)
    {
        foreach (var rule in _rules.Rules)
        {
            if (rule.Direction != RoutingDirection.DeviceToBroker)
            {
                continue;
            }

            var match = rule.Compiled.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var topic = RoutingRule.Expand(rule.Topic, match);
            var payload = rule.Payload is null ? line : RoutingRule.Expand(rule.Payload, match);
            Hit(rule);
            Record(RoutingDirection.DeviceToBroker, topic, payload);
            // Fire-and-forget: Render runs on the session's read loop, which must not block on a broker.
            _ = PublishAsync(topic, payload);
            return;
        }

        Interlocked.Increment(ref _unmatched);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task PublishAsync(string topic, string payload)
    {
        try
        {
            await _sink.PublishAsync(topic, Encoding.UTF8.GetBytes(payload)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Router publish failed: {ex.Message}");
        }
    }

    private void Record(RoutingDirection direction, string topic, string payload)
    {
        lock (_gate)
        {
            _history.Add(new RoutedMessage(_time.GetUtcNow(), direction, topic, payload));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
