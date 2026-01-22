namespace StackExchange.Redis;

/// <summary>
/// Represents a message that is broadcast via publish/subscribe.
/// </summary>
public readonly struct ChannelMessage
{
    // this is *smaller* than storing a RedisChannel for the subscribed channel
    private readonly ChannelMessageQueue _queue;

    /// <summary>
    /// The Channel:Message string representation.
    /// </summary>
    public override string ToString() => ((string?)Channel) + ":" + ((string?)Message);

    /// <inheritdoc/>
    public override int GetHashCode() => Channel.GetHashCode() ^ Message.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ChannelMessage cm
                                                && cm.Channel == Channel && cm.Message == Message;

    internal ChannelMessage(ChannelMessageQueue queue, in RedisChannel channel, in RedisValue value)
    {
        _queue = queue;
        _channel = channel;
        _message = value;
    }

    /// <summary>
    /// The channel that the subscription was created from.
    /// </summary>
    public RedisChannel SubscriptionChannel => _queue.Channel;

    private readonly RedisChannel _channel;

    /// <summary>
    /// The channel that the message was broadcast to.
    /// </summary>
    public RedisChannel Channel => _channel;

    private readonly RedisValue _message;

    /// <summary>
    /// The value that was broadcast.
    /// </summary>
    public RedisValue Message => _message;

    /// <summary>
    /// Checks if 2 messages are .Equal().
    /// </summary>
    public static bool operator ==(ChannelMessage left, ChannelMessage right) => left.Equals(right);

    /// <summary>
    /// Checks if 2 messages are not .Equal().
    /// </summary>
    public static bool operator !=(ChannelMessage left, ChannelMessage right) => !left.Equals(right);

    /// <summary>
    /// If the channel is either a keyspace or keyevent notification, resolve the key and event type.
    /// </summary>
    public bool TryParseKeyNotification(out KeyNotification notification)
        => KeyNotification.TryParse(in _channel, in _message, out notification);
}
