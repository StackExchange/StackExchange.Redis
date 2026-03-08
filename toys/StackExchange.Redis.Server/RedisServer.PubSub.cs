using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace StackExchange.Redis.Server;

public partial class RedisServer
{
    protected virtual void OnOutOfBand(RedisClient client, TypedRedisValue message)
        => client.AddOutbound(message);

    [RedisCommand(-2)]
    protected virtual TypedRedisValue Subscribe(RedisClient client, in RedisRequest request)
        => SubscribeImpl(client, request, RedisCommand.SUBSCRIBE);

    [RedisCommand(-1)]
    protected virtual TypedRedisValue Unsubscribe(RedisClient client, in RedisRequest request)
        => SubscribeImpl(client, request, RedisCommand.UNSUBSCRIBE);

    [RedisCommand(-2)]
    protected virtual TypedRedisValue PSubscribe(RedisClient client, in RedisRequest request)
        => SubscribeImpl(client, request, RedisCommand.PSUBSCRIBE);

    [RedisCommand(-1)]
    protected virtual TypedRedisValue PUnsubscribe(RedisClient client, in RedisRequest request)
        => SubscribeImpl(client, request, RedisCommand.PUNSUBSCRIBE);

    [RedisCommand(-2)]
    protected virtual TypedRedisValue SSubscribe(RedisClient client, in RedisRequest request)
        => SubscribeImpl(client, request, RedisCommand.SSUBSCRIBE);

    [RedisCommand(-1)]
    protected virtual TypedRedisValue SUnsubscribe(RedisClient client, in RedisRequest request)
        => SubscribeImpl(client, request, RedisCommand.SUNSUBSCRIBE);

    [RedisCommand(3)]
    protected virtual TypedRedisValue Publish(RedisClient client, in RedisRequest request)
    {
        PublishPair pair = new(
            request.GetChannel(1, RedisChannel.RedisChannelOptions.None),
            request.GetValue(2));
        // note: docs say "the number of clients that the message was sent to.", but this is a lie; it
        // is the number of *subscriptions* - if a client has two matching: delta is two
        int count = ForAllClients(pair, static (client, pair) => client.Publish(pair.Channel, pair.Value));
        return TypedRedisValue.Integer(count);
    }

    private readonly struct PublishPair(RedisChannel channel, RedisValue value, Node node = null)
    {
        public readonly RedisChannel Channel = channel;
        public readonly RedisValue Value = value;
        public readonly Node Node = node;
    }
    [RedisCommand(3)]
    protected virtual TypedRedisValue SPublish(RedisClient client, in RedisRequest request)
    {
        var channel = request.GetChannel(1, RedisChannel.RedisChannelOptions.Sharded);
        var node = client.Node; // filter to clients on the same node
        var slot = ServerSelectionStrategy.GetClusterSlot((byte[])channel);
        if (!node.HasSlot(slot)) KeyMovedException.Throw(slot);

        PublishPair pair = new(channel, request.GetValue(2));
        int count = ForAllClients(pair, static (client, pair) =>
            ReferenceEquals(client.Node, pair.Node) ? client.Publish(pair.Channel, pair.Value) : 0);
        return TypedRedisValue.Integer(count);
    }

    private TypedRedisValue SubscribeImpl(RedisClient client, in RedisRequest request, RedisCommand cmd)
    {
        bool add = cmd is RedisCommand.SUBSCRIBE or RedisCommand.PSUBSCRIBE or RedisCommand.SSUBSCRIBE;
        var options = cmd switch
        {
            RedisCommand.SUBSCRIBE or RedisCommand.UNSUBSCRIBE => RedisChannel.RedisChannelOptions.None,
            RedisCommand.PSUBSCRIBE or RedisCommand.PUNSUBSCRIBE => RedisChannel.RedisChannelOptions.Pattern,
            RedisCommand.SSUBSCRIBE or RedisCommand.SUNSUBSCRIBE => RedisChannel.RedisChannelOptions.Sharded,
            _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
        };

        // buffer the slots while checking validity
        var subCount = request.Count - 1;
        if (subCount == 0 & !add)
        {
            client.UnsubscribeAll(cmd);
        }
        else
        {
            var lease = ArrayPool<RedisChannel>.Shared.Rent(subCount);
            try
            {
                var channel = lease[0] = request.GetChannel(1, options);
                int slot = channel.IsSharded
                    ? ServerSelectionStrategy.GetClusterSlot(channel)
                    : ServerSelectionStrategy.NoSlot;
                if (!client.Node.HasSlot(slot)) KeyMovedException.Throw(slot);
                for (int i = 2; i <= subCount; i++)
                {
                    channel = lease[i - 1] = request.GetChannel(i, options);
                    if (slot != ServerSelectionStrategy.NoSlot &&
                        slot != ServerSelectionStrategy.GetClusterSlot(channel))
                    {
                        CrossSlotException.Throw();
                    }
                }

                for (int i = 0; i < subCount; i++)
                {
                    if (add) client.Subscribe(lease[i]);
                    else client.Unsubscribe(lease[i]);
                }
            }
            finally
            {
                ArrayPool<RedisChannel>.Shared.Return(lease);
            }
        }

        return TypedRedisValue.Nil;
    }
}

public partial class RedisClient
{
    private bool HasSubscriptions
    {
        get
        {
            var subs = _subscriptions;
            if (subs is null) return false;
            lock (subs)
            {
                return subs.Count != 0;
            }
        }
    }

    private Dictionary<RedisChannel, Regex> SubscriptionsIfAny
    {
        get
        {
            var subs = _subscriptions;
            if (subs is not null)
            {
                lock (subs)
                {
                    if (subs.Count == 0) return null;
                }
            }
            return subs;
        }
    }
    private Dictionary<RedisChannel, Regex> Subscriptions
    {
        get
        {
            return _subscriptions ?? InitSubs();

            Dictionary<RedisChannel, Regex> InitSubs()
            {
                var newSubs = new Dictionary<RedisChannel, Regex>();
                return Interlocked.CompareExchange(ref _subscriptions, newSubs, null) ?? newSubs;
            }
        }
    }

    private int simpleCount, shardedCount, patternCount;
    private Dictionary<RedisChannel, Regex> _subscriptions;
    public int SubscriptionCount => simpleCount;
    public int ShardedSubscriptionCount => shardedCount;
    public int PatternSubscriptionCount => patternCount;
    public bool IsSubscriber => (SubscriptionCount + ShardedSubscriptionCount + PatternSubscriptionCount) != 0;

    public int Publish(in RedisChannel channel, in RedisValue value)
    {
        var node = Node;
        if (node is null) return 0;
        int count = 0;
        var subs = Subscriptions;
        lock (subs)
        {
            // we can do simple and sharded equality lookups directly
            if ((simpleCount + shardedCount) != 0 && subs.TryGetValue(channel, out _))
            {
                var msg = TypedRedisValue.Rent(3,  out var span, ResultType.Push);
                span[0] = TypedRedisValue.BulkString(channel.IsSharded ? "smessage" : "message");
                span[1] = TypedRedisValue.BulkString(channel);
                span[2] = TypedRedisValue.BulkString(value);
                node.OnOutOfBand(this, msg);
                count++;
            }

            if (patternCount != 0 && !channel.IsSharded)
            {
                // need to loop for patterns
                var channelName = channel.ToString();
                foreach (var pair in subs)
                {
                    if (pair.Key.IsPattern && pair.Value is { } glob && glob.IsMatch(channelName))
                    {
                        var msg = TypedRedisValue.Rent(4,  out var span, ResultType.Push);
                        span[0] = TypedRedisValue.BulkString("pmessage");
                        span[1] = TypedRedisValue.BulkString(pair.Key);
                        span[2] = TypedRedisValue.BulkString(channel);
                        span[3] = TypedRedisValue.BulkString(value);
                        node.OnOutOfBand(this, msg);
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private void SendMessage(string kind, RedisChannel channel, int count)
    {
        if (Node is { } node)
        {
            var reply = TypedRedisValue.Rent(3, out var span, ResultType.Push);
            span[0] = TypedRedisValue.BulkString(kind);
            span[1] = TypedRedisValue.BulkString((byte[])channel);
            span[2] = TypedRedisValue.Integer(count);
            // go via node to allow logging etc
            node.OnOutOfBand(this, reply);
        }
    }

    internal void Subscribe(RedisChannel channel)
    {
        Regex glob = channel.IsPattern ? BuildGlob(channel) : null;
        var subs = Subscriptions;
        int count;
        lock (subs)
        {
            if (subs.ContainsKey(channel)) return;
            subs.Add(channel, glob);
            count = channel.IsSharded ? ++shardedCount
                : channel.IsPattern ? ++patternCount
                : ++simpleCount;
        }
        SendMessage(
            channel.IsSharded ? "ssubscribe"
            : channel.IsPattern ? "psubscribe"
            : "subscribe",
            channel,
            count);
    }

    private Regex BuildGlob(RedisChannel channel)
    {
        /* supported patterns:
         h?llo subscribes to hello, hallo and hxllo
         h*llo subscribes to hllo and heeeello
         h[ae]llo subscribes to hello and hallo, but not hillo
         */
        // firstly, escape *everything*, then we'll go back and fixup
        var re = Regex.Escape(channel.ToString());
        re = re.Replace(@"\?", ".").Replace(@"\*", ".*")
            .Replace(@"\[",  "[").Replace(@"\]", "]"); // not perfect, but good enough for now
        return new Regex(re, RegexOptions.CultureInvariant);
    }

    internal void Unsubscribe(RedisChannel channel)
    {
        var subs = SubscriptionsIfAny;
        if (subs is null) return;
        int count;
        lock (subs)
        {
            if (!subs.Remove(channel)) return;
            count = channel.IsSharded ? --shardedCount
                : channel.IsPattern ? --patternCount
                : --simpleCount;
        }
        SendMessage(
            channel.IsSharded ? "sunsubscribe"
            : channel.IsPattern ? "punsubscribe"
            : "unsubscribe",
            channel,
            count);
    }

    internal void UnsubscribeAll(RedisCommand cmd)
    {
        var subs = Subscriptions;
        if (subs is null) return;
        RedisChannel[] remove;
        int count = 0;
        string msg;
        lock (subs)
        {
            remove = ArrayPool<RedisChannel>.Shared.Rent(count);
            foreach (var pair in subs)
            {
                var key = pair.Key;
                if (cmd switch
                    {
                        RedisCommand.UNSUBSCRIBE when !(pair.Key.IsPattern | pair.Key.IsSharded) => true,
                        RedisCommand.PUNSUBSCRIBE when pair.Key.IsPattern => true,
                        RedisCommand.SUNSUBSCRIBE when pair.Key.IsSharded => true,
                        _ => false,
                    })
                {
                    remove[count++] = key;
                }
            }

            foreach (var key in remove.AsSpan(0, count))
            {
                _subscriptions.Remove(key);
            }

            switch (cmd)
            {
                case RedisCommand.SUNSUBSCRIBE:
                    msg = "sunsubscribe";
                    shardedCount = 0;
                    break;
                case RedisCommand.PUNSUBSCRIBE:
                    msg = "punsubscribe";
                    patternCount = 0;
                    break;
                case RedisCommand.UNSUBSCRIBE:
                    msg = "unsubscribe";
                    simpleCount = 0;
                    break;
                default:
                    msg = "";
                    break;
            }
        }
        foreach (var key in remove.AsSpan(0, count))
        {
            SendMessage(msg, key, 0);
        }
        ArrayPool<RedisChannel>.Shared.Return(remove);
    }
}
