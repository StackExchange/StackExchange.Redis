using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Stream Async operations
    public Task<StreamAutoClaimIdsOnlyResult> StreamAutoClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAutoClaimIdsOnlyAsync(key, consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, flags);

    public Task<StreamAutoClaimResult> StreamAutoClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAutoClaimAsync(key, consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, flags);

    public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAddAsync(key, streamField, streamValue, messageId, maxLength, useApproximateMaxLength, flags);

    public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAddAsync(key, streamField, streamValue, messageId, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAddAsync(key, streamPairs, messageId, maxLength, useApproximateMaxLength, flags);

    public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAddAsync(key, streamPairs, messageId, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, StreamIdempotentId idempotentId, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAddAsync(key, streamField, streamValue, idempotentId, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, StreamIdempotentId idempotentId, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAddAsync(key, streamPairs, idempotentId, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public Task<StreamEntry[]> StreamClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamClaimAsync(key, consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags);

    public Task<RedisValue[]> StreamClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamClaimIdsOnlyAsync(key, consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags);

    public Task StreamConfigureAsync(RedisKey key, StreamConfiguration configuration, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamConfigureAsync(key, configuration, flags);

    public Task<bool> StreamConsumerGroupSetPositionAsync(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamConsumerGroupSetPositionAsync(key, groupName, position, flags);

    public Task<StreamConsumerInfo[]> StreamConsumerInfoAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamConsumerInfoAsync(key, groupName, flags);

    public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
        => GetActiveDatabase().StreamCreateConsumerGroupAsync(key, groupName, position, flags);

    public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamCreateConsumerGroupAsync(key, groupName, position, createStream, flags);

    public Task<long> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamDeleteAsync(key, messageIds, flags);

    public Task<StreamTrimResult[]> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, StreamTrimMode mode, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamDeleteAsync(key, messageIds, mode, flags);

    public Task<long> StreamDeleteConsumerAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamDeleteConsumerAsync(key, groupName, consumerName, flags);

    public Task<bool> StreamDeleteConsumerGroupAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamDeleteConsumerGroupAsync(key, groupName, flags);

    public Task<StreamGroupInfo[]> StreamGroupInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamGroupInfoAsync(key, flags);

    public Task<StreamInfo> StreamInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamInfoAsync(key, flags);

    public Task<long> StreamLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamLengthAsync(key, flags);

    public Task<StreamPendingInfo> StreamPendingAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamPendingAsync(key, groupName, flags);

    public Task<StreamPendingMessageInfo[]> StreamPendingMessagesAsync(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamPendingMessagesAsync(key, groupName, count, consumerName, minId, maxId, flags);

    public Task<StreamPendingMessageInfo[]> StreamPendingMessagesAsync(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, long? idle = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamPendingMessagesAsync(key, groupName, count, consumerName, minId, maxId, idle, flags);

    public Task<StreamEntry[]> StreamRangeAsync(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamRangeAsync(key, minId, maxId, count, messageOrder, flags);

    public Task<StreamEntry[]> StreamReadAsync(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadAsync(key, position, count, flags);

    public Task<RedisStream[]> StreamReadAsync(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadAsync(streamPositions, countPerStream, flags);

    public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags)
        => GetActiveDatabase().StreamReadGroupAsync(key, groupName, consumerName, position, count, flags);

    public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadGroupAsync(key, groupName, consumerName, position, count, noAck, flags);

    public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, TimeSpan? blockingTimeout = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadGroupAsync(key, groupName, consumerName, position, count, noAck, blockingTimeout, flags);

    public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags)
        => GetActiveDatabase().StreamReadGroupAsync(streamPositions, groupName, consumerName, countPerStream, flags);

    public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadGroupAsync(streamPositions, groupName, consumerName, countPerStream, noAck, flags);

    public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, TimeSpan? blockingTimeout = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadGroupAsync(streamPositions, groupName, consumerName, countPerStream, noAck, blockingTimeout, flags);

    public Task<long> StreamTrimAsync(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamTrimAsync(key, maxLength, useApproximateMaxLength, flags);

    public Task<long> StreamTrimAsync(RedisKey key, long maxLength, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamTrimAsync(key, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public Task<long> StreamTrimByMinIdAsync(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamTrimByMinIdAsync(key, minId, useApproximateMaxLength, limit, trimMode, flags);

    public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAcknowledgeAsync(key, groupName, messageId, flags);

    public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAcknowledgeAsync(key, groupName, messageIds, flags);

    public Task<StreamTrimResult> StreamAcknowledgeAndDeleteAsync(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAcknowledgeAndDeleteAsync(key, groupName, mode, messageId, flags);

    public Task<StreamTrimResult[]> StreamAcknowledgeAndDeleteAsync(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAcknowledgeAndDeleteAsync(key, groupName, mode, messageIds, flags);
}
