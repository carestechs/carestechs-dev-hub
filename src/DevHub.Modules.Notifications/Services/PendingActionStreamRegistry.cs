using System.Collections.Concurrent;
using System.Threading.Channels;
using DevHub.Contracts.Notifications;

namespace DevHub.Modules.Notifications.Services;

/// <summary>
/// Process-local fan-out from the reconciler to SSE subscribers. One bounded-by-process
/// channel per active subscription; multiple subscriptions per member are allowed (mobile +
/// desktop in the same session).
///
/// **v1 single-host caveat:** this registry is in-process state. Scale-out across multiple API
/// hosts is a v2 concern — Redis pub/sub, NATS, or similar. v1 assumes single-host.
/// </summary>
public sealed class PendingActionStreamRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<PendingActionEvent>>> _subscribers = new();

    /// <summary>
    /// Subscribe a new SSE reader for <paramref name="memberId"/>. Returns the reader and an
    /// <see cref="IAsyncDisposable"/> that removes the subscription + completes the channel on
    /// disposal. The caller MUST <c>await using</c> the returned handle.
    /// </summary>
    public IAsyncDisposable Subscribe(Guid memberId, out ChannelReader<PendingActionEvent> reader)
    {
        var channel = Channel.CreateUnbounded<PendingActionEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
        reader = channel.Reader;

        var subId = Guid.NewGuid();
        var bag = _subscribers.GetOrAdd(memberId, _ => new ConcurrentDictionary<Guid, Channel<PendingActionEvent>>());
        bag[subId] = channel;
        return new Cleanup(this, memberId, subId, channel);
    }

    /// <summary>
    /// Publish an event to every currently-subscribed reader for the event's <c>MemberId</c>.
    /// No-op if there are no subscribers.
    /// </summary>
    public void Publish(PendingActionEvent ev)
    {
        if (!_subscribers.TryGetValue(ev.MemberId, out var bag)) return;
        foreach (var channel in bag.Values)
        {
            channel.Writer.TryWrite(ev);
        }
    }

    private sealed class Cleanup(
        PendingActionStreamRegistry owner,
        Guid memberId,
        Guid subId,
        Channel<PendingActionEvent> channel) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            channel.Writer.TryComplete();
            if (owner._subscribers.TryGetValue(memberId, out var bag))
            {
                bag.TryRemove(subId, out _);
                if (bag.IsEmpty) owner._subscribers.TryRemove(memberId, out _);
            }
            return ValueTask.CompletedTask;
        }
    }
}
