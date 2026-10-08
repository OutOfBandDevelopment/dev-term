using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace DevTerm.Web;

/// <summary>
/// The host's event stream behind <c>GET /api/events</c> (Server-Sent Events): connections opened and closed through
/// <c>/api/connections</c>, and the lines the main session prints. Each subscriber gets its own bounded queue, so a slow
/// browser drops its oldest events instead of stalling the host.
/// </summary>
public sealed class HostEvents
{
    private readonly ConcurrentDictionary<Channel<string>, byte> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    /// <summary>Publishes one event to every current subscriber. <paramref name="data"/> is serialized as JSON.</summary>
    public void Publish(string name, object data)
    {
        var frame = $"event: {name}\ndata: {JsonSerializer.Serialize(data)}\n\n";
        foreach (var channel in _subscribers.Keys)
        {
            channel.Writer.TryWrite(frame);
        }
    }

    /// <summary>Streams events as they happen until <paramref name="cancellationToken"/> fires.</summary>
    public async IAsyncEnumerable<string> SubscribeAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[channel] = 0;
        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }
        finally
        {
            _subscribers.TryRemove(channel, out _);
        }
    }
}
