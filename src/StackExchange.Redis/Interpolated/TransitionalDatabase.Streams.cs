using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The stream commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// The scalar half only; the reads return composites holding arrays and have not moved yet. Note the
    /// array parameters here: the old signatures promise <c>RedisValue[]</c>, so this is where an array
    /// becomes a span - the conversion belongs to the adapter, not to the new surface.
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public long StreamLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<long> StreamLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Streams.LengthAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.AcknowledgeAsync(key, groupName, messageId, flags));

        /// <inheritdoc/>
        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => Context.Streams.AcknowledgeAsync(key, groupName, messageId, flags).AsTask();

        /// <inheritdoc/>
        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.AcknowledgeAsync(key, groupName, Required(messageIds, nameof(messageIds)), flags));

        /// <inheritdoc/>
        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => Context.Streams.AcknowledgeAsync(key, groupName, Required(messageIds, nameof(messageIds)), flags).AsTask();

        /// <inheritdoc/>
        public long StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.DeleteAsync(key, Required(messageIds, nameof(messageIds)), flags));

        /// <inheritdoc/>
        public Task<long> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => Context.Streams.DeleteAsync(key, Required(messageIds, nameof(messageIds)), flags).AsTask();

        /// <inheritdoc/>
        public StreamTrimResult[] StreamDelete(RedisKey key, RedisValue[] messageIds, StreamTrimMode mode, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.DeleteArray(key, Required(messageIds, nameof(messageIds)), mode, flags));

        /// <inheritdoc/>
        public Task<StreamTrimResult[]> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, StreamTrimMode mode, CommandFlags flags = CommandFlags.None)
            => Context.Streams.DeleteArray(key, Required(messageIds, nameof(messageIds)), mode, flags).AsTask();

        /// <inheritdoc/>
        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
            => Wait(Context.Streams.CreateConsumerGroupAsync(key, groupName, position, createStream: true, flags));

        /// <inheritdoc/>
        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
            => Context.Streams.CreateConsumerGroupAsync(key, groupName, position, createStream: true, flags).AsTask();

        /// <inheritdoc/>
        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.CreateConsumerGroupAsync(key, groupName, position, createStream, flags));

        /// <inheritdoc/>
        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
            => Context.Streams.CreateConsumerGroupAsync(key, groupName, position, createStream, flags).AsTask();

        /// <inheritdoc/>
        public bool StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.DeleteConsumerGroupAsync(key, groupName, flags));

        /// <inheritdoc/>
        public Task<bool> StreamDeleteConsumerGroupAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
            => Context.Streams.DeleteConsumerGroupAsync(key, groupName, flags).AsTask();

        /// <inheritdoc/>
        public long StreamDeleteConsumer(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.DeleteConsumerAsync(key, groupName, consumerName, flags));

        /// <inheritdoc/>
        public Task<long> StreamDeleteConsumerAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
            => Context.Streams.DeleteConsumerAsync(key, groupName, consumerName, flags).AsTask();

        /// <inheritdoc/>
        public bool StreamConsumerGroupSetPosition(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.SetConsumerGroupPositionAsync(key, groupName, position, flags));

        /// <inheritdoc/>
        public Task<bool> StreamConsumerGroupSetPositionAsync(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
            => Context.Streams.SetConsumerGroupPositionAsync(key, groupName, position, flags).AsTask();

        /// <inheritdoc/>
        public long StreamTrim(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => Wait(Context.Streams.TrimAsync(key, maxLength, useApproximateMaxLength, flags: flags));

        /// <inheritdoc/>
        public Task<long> StreamTrimAsync(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => Context.Streams.TrimAsync(key, maxLength, useApproximateMaxLength, flags: flags).AsTask();

        /// <inheritdoc/>
        public long StreamTrim(RedisKey key, long maxLength, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.TrimAsync(key, maxLength, useApproximateMaxLength, limit, mode, flags));

        /// <inheritdoc/>
        public Task<long> StreamTrimAsync(RedisKey key, long maxLength, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => Context.Streams.TrimAsync(key, maxLength, useApproximateMaxLength, limit, mode, flags).AsTask();

        /// <inheritdoc/>
        public long StreamTrimByMinId(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Streams.TrimByMinIdAsync(key, minId, useApproximateMaxLength, limit, mode, flags));

        /// <inheritdoc/>
        public Task<long> StreamTrimByMinIdAsync(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => Context.Streams.TrimByMinIdAsync(key, minId, useApproximateMaxLength, limit, mode, flags).AsTask();
    }
}
