using System;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Stream operations
    public StreamAutoClaimIdsOnlyResult StreamAutoClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAutoClaimIdsOnly(key, consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, flags);

    public StreamAutoClaimResult StreamAutoClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAutoClaim(key, consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, flags);

    public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAdd(key, streamField, streamValue, messageId, maxLength, useApproximateMaxLength, flags);

    public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAdd(key, streamField, streamValue, messageId, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAdd(key, streamPairs, messageId, maxLength, useApproximateMaxLength, flags);

    public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAdd(key, streamPairs, messageId, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, StreamIdempotentId idempotentId, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAdd(key, streamField, streamValue, idempotentId, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, StreamIdempotentId idempotentId, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAdd(key, streamPairs, idempotentId, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public StreamEntry[] StreamClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamClaim(key, consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags);

    public RedisValue[] StreamClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamClaimIdsOnly(key, consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags);

    public void StreamConfigure(RedisKey key, StreamConfiguration configuration, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamConfigure(key, configuration, flags);

    public bool StreamConsumerGroupSetPosition(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamConsumerGroupSetPosition(key, groupName, position, flags);

    public StreamConsumerInfo[] StreamConsumerInfo(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamConsumerInfo(key, groupName, flags);

    public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
        => GetActiveDatabase().StreamCreateConsumerGroup(key, groupName, position, flags);

    public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamCreateConsumerGroup(key, groupName, position, createStream, flags);

    public long StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamDelete(key, messageIds, flags);

    public long StreamDeleteConsumer(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamDeleteConsumer(key, groupName, consumerName, flags);

    public bool StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamDeleteConsumerGroup(key, groupName, flags);

    public StreamGroupInfo[] StreamGroupInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamGroupInfo(key, flags);

    public StreamInfo StreamInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamInfo(key, flags);

    public long StreamLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamLength(key, flags);

    public StreamPendingInfo StreamPending(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamPending(key, groupName, flags);

    public StreamPendingMessageInfo[] StreamPendingMessages(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamPendingMessages(key, groupName, count, consumerName, minId, maxId, flags);

    public StreamEntry[] StreamRange(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamRange(key, minId, maxId, count, messageOrder, flags);

    public StreamEntry[] StreamRead(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamRead(key, position, count, flags);

    public RedisStream[] StreamRead(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamRead(streamPositions, countPerStream, flags);

    public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags)
        => GetActiveDatabase().StreamReadGroup(key, groupName, consumerName, position, count, flags);

    public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadGroup(key, groupName, consumerName, position, count, noAck, flags);

    public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags)
        => GetActiveDatabase().StreamReadGroup(streamPositions, groupName, consumerName, countPerStream, flags);

    public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadGroup(streamPositions, groupName, consumerName, countPerStream, noAck, flags);

    public long StreamTrim(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamTrim(key, maxLength, useApproximateMaxLength, flags);

    public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAcknowledge(key, groupName, messageId, flags);

    public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAcknowledge(key, groupName, messageIds, flags);

    public StreamTrimResult StreamAcknowledgeAndDelete(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAcknowledgeAndDelete(key, groupName, mode, messageId, flags);

    public StreamTrimResult[] StreamAcknowledgeAndDelete(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamAcknowledgeAndDelete(key, groupName, mode, messageIds, flags);

    public StreamTrimResult[] StreamDelete(RedisKey key, RedisValue[] messageIds, StreamTrimMode mode, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamDelete(key, messageIds, mode, flags);

    public StreamPendingMessageInfo[] StreamPendingMessages(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, long? idle = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamPendingMessages(key, groupName, count, consumerName, minId, maxId, idle, flags);

    public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, TimeSpan? blockingTimeout = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadGroup(key, groupName, consumerName, position, count, noAck, blockingTimeout, flags);

    public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, TimeSpan? blockingTimeout = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamReadGroup(streamPositions, groupName, consumerName, countPerStream, noAck, blockingTimeout, flags);

    public long StreamTrim(RedisKey key, long maxLength, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamTrim(key, maxLength, useApproximateMaxLength, limit, trimMode, flags);

    public long StreamTrimByMinId(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StreamTrimByMinId(key, minId, useApproximateMaxLength, limit, trimMode, flags);
}
