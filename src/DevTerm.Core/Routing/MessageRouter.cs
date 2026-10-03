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

    public MessageRouter(RoutingRuleSet rules, IMessageSink sink, TimeProvider? time = null, string terminator = "\r\n")
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(sink);
        _rules = rules;
        _sink = sink;
        _time = time ?? TimeProvider.System;
        _terminator = terminator;
    }

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

            Record(RoutingDirection.BrokerToDevice, topic, payload);
            Originated?.Invoke(this, Encoding.ASCII.GetBytes(RoutingRule.Expand(rule.Send, match) + _terminator));
            return;
        }
    }

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
            Record(RoutingDirection.DeviceToBroker, topic, payload);
            // Fire-and-forget: Render runs on the session's read loop, which must not block on a broker.
            _ = PublishAsync(topic, payload);
            return;
        }
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
    }
}
