namespace DevTerm.Core.Routing;

/// <summary>Where a <see cref="MessageRouter"/> publishes device messages: a broker connection, or a fake in tests.</summary>
public interface IMessageSink
{
    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

/// <summary>One routed message, stamped with the clock tick it was posted (shared by every channel).</summary>
public sealed record RoutedMessage(DateTimeOffset Timestamp, RoutingDirection Direction, string Topic, string Payload);
