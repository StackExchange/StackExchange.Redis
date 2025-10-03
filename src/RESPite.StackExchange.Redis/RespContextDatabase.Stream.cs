using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    // Async Stream methods
    public Task<long> StreamAcknowledgeAsync(
        RedisKey key,
        RedisValue groupName,
        RedisValue messageId,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StreamAcknowledgeAsync(
        RedisKey key,
        RedisValue groupName,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamTrimResult> StreamAcknowledgeAndDeleteAsync(
        RedisKey key,
        RedisValue groupName,
        StreamTrimMode trimMode,
        RedisValue messageId,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamTrimResult[]> StreamAcknowledgeAndDeleteAsync(
        RedisKey key,
        RedisValue groupName,
        StreamTrimMode trimMode,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StreamAddAsync(
        RedisKey key,
        RedisValue streamField,
        RedisValue streamValue,
        RedisValue? messageId = null,
        int? maxLength = null,
        bool useApproximateMaxLength = false,
        CommandFlags flags = CommandFlags.None) =>
        (messageId is null & maxLength is null & !useApproximateMaxLength)
            ? StreamAddSimpleCoreAsync(key, streamField, streamValue, flags)
            : throw new NotImplementedException();

    [RespCommand("xadd")]
    private partial RedisValue StreamAddSimpleCore(
        RedisKey key,
        [RespPrefix("*")]
        RedisValue streamField,
        RedisValue streamValue,
        CommandFlags flags = CommandFlags.None);

    public Task<RedisValue> StreamAddAsync(
        RedisKey key,
        NameValueEntry[] streamPairs,
        RedisValue? messageId = null,
        int? maxLength = null,
        bool useApproximateMaxLength = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StreamAddAsync(
        RedisKey key,
        RedisValue streamField,
        RedisValue streamValue,
        RedisValue? messageId = null,
        long? maxLength = null,
        bool useApproximateMaxLength = false,
        long? limit = null,
        StreamTrimMode trimMode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None) =>
        (messageId is null & maxLength is null & !useApproximateMaxLength
         & limit is null & trimMode == StreamTrimMode.KeepReferences)
            ? StreamAddSimpleCoreAsync(key, streamField, streamValue, flags)
            : throw new NotImplementedException();

    public Task<RedisValue> StreamAddAsync(
        RedisKey key,
        NameValueEntry[] streamPairs,
        RedisValue? messageId = null,
        long? maxLength = null,
        bool useApproximateMaxLength = false,
        long? limit = null,
        StreamTrimMode trimMode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamAutoClaimResult> StreamAutoClaimAsync(
        RedisKey key,
        RedisValue consumerGroup,
        RedisValue claimingConsumer,
        long minIdleTimeInMs,
        RedisValue startAtId,
        int? count = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamAutoClaimIdsOnlyResult> StreamAutoClaimIdsOnlyAsync(
        RedisKey key,
        RedisValue consumerGroup,
        RedisValue claimingConsumer,
        long minIdleTimeInMs,
        RedisValue startAtId,
        int? count = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamEntry[]> StreamClaimAsync(
        RedisKey key,
        RedisValue consumerGroup,
        RedisValue claimingConsumer,
        long minIdleTimeInMs,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> StreamClaimIdsOnlyAsync(
        RedisKey key,
        RedisValue consumerGroup,
        RedisValue claimingConsumer,
        long minIdleTimeInMs,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> StreamConsumerGroupSetPositionAsync(
        RedisKey key,
        RedisValue groupName,
        RedisValue position,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamConsumerInfo[]> StreamConsumerInfoAsync(
        RedisKey key,
        RedisValue groupName,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> StreamCreateConsumerGroupAsync(
        RedisKey key,
        RedisValue groupName,
        RedisValue? position = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> StreamCreateConsumerGroupAsync(
        RedisKey key,
        RedisValue groupName,
        RedisValue? position = null,
        bool createStream = true,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StreamDeleteAsync(
        RedisKey key,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamTrimResult[]> StreamDeleteAsync(
        RedisKey key,
        RedisValue[] messageIds,
        StreamTrimMode trimMode,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StreamDeleteConsumerAsync(
        RedisKey key,
        RedisValue groupName,
        RedisValue consumerName,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> StreamDeleteConsumerGroupAsync(
        RedisKey key,
        RedisValue groupName,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamGroupInfo[]> StreamGroupInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamInfo> StreamInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StreamLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamPendingInfo> StreamPendingAsync(
        RedisKey key,
        RedisValue groupName,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamPendingMessageInfo[]> StreamPendingMessagesAsync(
        RedisKey key,
        RedisValue groupName,
        int count,
        RedisValue consumerName,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamPendingMessageInfo[]> StreamPendingMessagesAsync(
        RedisKey key,
        RedisValue groupName,
        int count,
        RedisValue consumerName,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        long? idleTime = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamEntry[]> StreamRangeAsync(
        RedisKey key,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        int? count = null,
        Order messageOrder = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamEntry[]> StreamReadAsync(
        RedisKey key,
        RedisValue position,
        int? count = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisStream[]> StreamReadAsync(
        StreamPosition[] streamPositions,
        int? countPerStream = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamEntry[]> StreamReadGroupAsync(
        RedisKey key,
        RedisValue groupName,
        RedisValue consumerName,
        RedisValue? position = null,
        int? count = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<StreamEntry[]> StreamReadGroupAsync(
        RedisKey key,
        RedisValue groupName,
        RedisValue consumerName,
        RedisValue? position = null,
        int? count = null,
        bool noAck = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisStream[]> StreamReadGroupAsync(
        StreamPosition[] streamPositions,
        RedisValue groupName,
        RedisValue consumerName,
        int? countPerStream = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisStream[]> StreamReadGroupAsync(
        StreamPosition[] streamPositions,
        RedisValue groupName,
        RedisValue consumerName,
        int? countPerStream = null,
        bool noAck = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StreamTrimAsync(
        RedisKey key,
        int maxLength,
        bool useApproximateMaxLength = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StreamTrimAsync(
        RedisKey key,
        long maxLength,
        bool useApproximateMaxLength = false,
        long? limit = null,
        StreamTrimMode trimMode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StreamTrimByMinIdAsync(
        RedisKey key,
        RedisValue minId,
        bool useApproximateMaxLength = false,
        long? limit = null,
        StreamTrimMode trimMode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous Stream methods
    public long StreamAcknowledge(
        RedisKey key,
        RedisValue groupName,
        RedisValue messageId,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StreamAcknowledge(
        RedisKey key,
        RedisValue groupName,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamTrimResult StreamAcknowledgeAndDelete(
        RedisKey key,
        RedisValue groupName,
        StreamTrimMode trimMode,
        RedisValue messageId,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamTrimResult[] StreamAcknowledgeAndDelete(
        RedisKey key,
        RedisValue groupName,
        StreamTrimMode trimMode,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StreamAdd(
        RedisKey key,
        RedisValue streamField,
        RedisValue streamValue,
        RedisValue? messageId = null,
        int? maxLength = null,
        bool useApproximateMaxLength = false,
        CommandFlags flags = CommandFlags.None) =>
        (messageId is null & maxLength is null & !useApproximateMaxLength)
            ? StreamAddSimpleCore(key, streamField, streamValue, flags)
            : throw new NotImplementedException();

    public RedisValue StreamAdd(
        RedisKey key,
        NameValueEntry[] streamPairs,
        RedisValue? messageId = null,
        int? maxLength = null,
        bool useApproximateMaxLength = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StreamAdd(
        RedisKey key,
        RedisValue streamField,
        RedisValue streamValue,
        RedisValue? messageId = null,
        long? maxLength = null,
        bool useApproximateMaxLength = false,
        long? limit = null,
        StreamTrimMode trimMode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None) =>
        (messageId is null & maxLength is null & !useApproximateMaxLength
         & limit is null & trimMode == StreamTrimMode.KeepReferences)
            ? StreamAddSimpleCore(key, streamField, streamValue, flags)
            : throw new NotImplementedException();

    public RedisValue StreamAdd(
        RedisKey key,
        NameValueEntry[] streamPairs,
        RedisValue? messageId = null,
        long? maxLength = null,
        bool useApproximateMaxLength = false,
        long? limit = null,
        StreamTrimMode trimMode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamAutoClaimResult StreamAutoClaim(
        RedisKey key,
        RedisValue consumerGroup,
        RedisValue claimingConsumer,
        long minIdleTimeInMs,
        RedisValue startAtId,
        int? count = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamAutoClaimIdsOnlyResult StreamAutoClaimIdsOnly(
        RedisKey key,
        RedisValue consumerGroup,
        RedisValue claimingConsumer,
        long minIdleTimeInMs,
        RedisValue startAtId,
        int? count = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamEntry[] StreamClaim(
        RedisKey key,
        RedisValue consumerGroup,
        RedisValue claimingConsumer,
        long minIdleTimeInMs,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] StreamClaimIdsOnly(
        RedisKey key,
        RedisValue consumerGroup,
        RedisValue claimingConsumer,
        long minIdleTimeInMs,
        RedisValue[] messageIds,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool StreamConsumerGroupSetPosition(
        RedisKey key,
        RedisValue groupName,
        RedisValue position,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamConsumerInfo[] StreamConsumerInfo(
        RedisKey key,
        RedisValue groupName,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool StreamCreateConsumerGroup(
        RedisKey key,
        RedisValue groupName,
        RedisValue? position = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool StreamCreateConsumerGroup(
        RedisKey key,
        RedisValue groupName,
        RedisValue? position = null,
        bool createStream = true,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamTrimResult[] StreamDelete(
        RedisKey key,
        RedisValue[] messageIds,
        StreamTrimMode trimMode,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StreamDeleteConsumer(
        RedisKey key,
        RedisValue groupName,
        RedisValue consumerName,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamGroupInfo[] StreamGroupInfo(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamInfo StreamInfo(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StreamLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamPendingInfo StreamPending(
        RedisKey key,
        RedisValue groupName,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamPendingMessageInfo[] StreamPendingMessages(
        RedisKey key,
        RedisValue groupName,
        int count,
        RedisValue consumerName,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamPendingMessageInfo[] StreamPendingMessages(
        RedisKey key,
        RedisValue groupName,
        int count,
        RedisValue consumerName,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        long? idleTime = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamEntry[] StreamRange(
        RedisKey key,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        int? count = null,
        Order messageOrder = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamEntry[] StreamRead(
        RedisKey key,
        RedisValue position,
        int? count = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisStream[] StreamRead(
        StreamPosition[] streamPositions,
        int? countPerStream = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamEntry[] StreamReadGroup(
        RedisKey key,
        RedisValue groupName,
        RedisValue consumerName,
        RedisValue? position = null,
        int? count = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public StreamEntry[] StreamReadGroup(
        RedisKey key,
        RedisValue groupName,
        RedisValue consumerName,
        RedisValue? position = null,
        int? count = null,
        bool noAck = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisStream[] StreamReadGroup(
        StreamPosition[] streamPositions,
        RedisValue groupName,
        RedisValue consumerName,
        int? countPerStream = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisStream[] StreamReadGroup(
        StreamPosition[] streamPositions,
        RedisValue groupName,
        RedisValue consumerName,
        int? countPerStream = null,
        bool noAck = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StreamTrim(
        RedisKey key,
        int maxLength,
        bool useApproximateMaxLength = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StreamTrim(
        RedisKey key,
        long maxLength,
        bool useApproximateMaxLength = false,
        long? limit = null,
        StreamTrimMode trimMode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StreamTrimByMinId(
        RedisKey key,
        RedisValue minId,
        bool useApproximateMaxLength = false,
        long? limit = null,
        StreamTrimMode trimMode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
