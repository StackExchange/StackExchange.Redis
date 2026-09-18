using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
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
        /// <remarks>
        /// <b>This is the escape hatch being exercised, not a sidecar.</b> <see cref="IDatabase"/> promises
        /// <see cref="StreamEntry"/><c>[]</c> and is not going anywhere, so the old shape is served by
        /// materialising the new one - which means <c>ToArray()</c> is covered by the whole existing stream
        /// suite rather than by whoever remembers to call it. The <c>using</c> is the cost of the
        /// transition: the reply owns a pooled buffer, and the array has to be taken before it goes back.
        /// </remarks>
        public StreamEntry[] StreamRange(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.RangeArray(key, minId, maxId, count, messageOrder, flags));

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="StreamRange(RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags)" path="/remarks"/></remarks>
        public Task<StreamEntry[]> StreamRangeAsync(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.RangeArray(key, minId, maxId, count, messageOrder, flags).AsTask();

        /// <inheritdoc/>
        public long StreamLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<long> StreamLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.LengthAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.AcknowledgeAsync(key, groupName, messageId, flags));

        /// <inheritdoc/>
        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.AcknowledgeAsync(key, groupName, messageId, flags).AsTask();

        /// <inheritdoc/>
        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.AcknowledgeAsync(key, groupName, Required(messageIds, nameof(messageIds)), flags));

        /// <inheritdoc/>
        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.AcknowledgeAsync(key, groupName, Required(messageIds, nameof(messageIds)), flags).AsTask();

        /// <inheritdoc/>
        public long StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.DeleteAsync(key, Required(messageIds, nameof(messageIds)), flags));

        /// <inheritdoc/>
        public Task<long> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.DeleteAsync(key, Required(messageIds, nameof(messageIds)), flags).AsTask();

        /// <inheritdoc/>
        public StreamTrimResult[] StreamDelete(RedisKey key, RedisValue[] messageIds, StreamTrimMode mode, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.DeleteArray(key, Required(messageIds, nameof(messageIds)), mode, flags));

        /// <inheritdoc/>
        public Task<StreamTrimResult[]> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, StreamTrimMode mode, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.DeleteArray(key, Required(messageIds, nameof(messageIds)), mode, flags).AsTask();

        /// <inheritdoc/>
        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
            => Wait(_inner.Streams.CreateConsumerGroupAsync(key, groupName, position, createStream: true, flags));

        /// <inheritdoc/>
        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
            => _inner.Streams.CreateConsumerGroupAsync(key, groupName, position, createStream: true, flags).AsTask();

        /// <inheritdoc/>
        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.CreateConsumerGroupAsync(key, groupName, position, createStream, flags));

        /// <inheritdoc/>
        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.CreateConsumerGroupAsync(key, groupName, position, createStream, flags).AsTask();

        /// <inheritdoc/>
        public bool StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.DeleteConsumerGroupAsync(key, groupName, flags));

        /// <inheritdoc/>
        public Task<bool> StreamDeleteConsumerGroupAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.DeleteConsumerGroupAsync(key, groupName, flags).AsTask();

        /// <inheritdoc/>
        public long StreamDeleteConsumer(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.DeleteConsumerAsync(key, groupName, consumerName, flags));

        /// <inheritdoc/>
        public Task<long> StreamDeleteConsumerAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.DeleteConsumerAsync(key, groupName, consumerName, flags).AsTask();

        /// <inheritdoc/>
        public bool StreamConsumerGroupSetPosition(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.SetConsumerGroupPositionAsync(key, groupName, position, flags));

        /// <inheritdoc/>
        public Task<bool> StreamConsumerGroupSetPositionAsync(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.SetConsumerGroupPositionAsync(key, groupName, position, flags).AsTask();

        /// <inheritdoc/>
        public long StreamTrim(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => Wait(_inner.Streams.TrimAsync(key, maxLength, useApproximateMaxLength, flags: flags));

        /// <inheritdoc/>
        public Task<long> StreamTrimAsync(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => _inner.Streams.TrimAsync(key, maxLength, useApproximateMaxLength, flags: flags).AsTask();

        /// <inheritdoc/>
        public long StreamTrim(RedisKey key, long maxLength, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.TrimAsync(key, maxLength, useApproximateMaxLength, limit, mode, flags));

        /// <inheritdoc/>
        public Task<long> StreamTrimAsync(RedisKey key, long maxLength, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.TrimAsync(key, maxLength, useApproximateMaxLength, limit, mode, flags).AsTask();

        /// <inheritdoc/>
        public long StreamTrimByMinId(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.TrimByMinIdAsync(key, minId, useApproximateMaxLength, limit, mode, flags));

        /// <inheritdoc/>
        public Task<long> StreamTrimByMinIdAsync(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.TrimByMinIdAsync(key, minId, useApproximateMaxLength, limit, mode, flags).AsTask();

        // ---- XADD -----------------------------------------------------------------------------------
        // Eight shipped overloads, two on the new surface. The difference is entirely StreamAddOptions:
        // the old spellings lay the same settings out positionally, and LegacyStreamAddOptions - shared
        // with the classic path rather than copied - folds them back up. It deliberately does NOT
        // validate, because the shipped overloads have always passed odd combinations to the server; only
        // the options-carrying overloads call ThrowIfInvalid, and that asymmetry is preserved here.

        /// <inheritdoc/>
        public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => StreamAdd(key, streamField, streamValue, messageId, maxLength, useApproximateMaxLength, null, StreamTrimMode.KeepReferences, flags);

        /// <inheritdoc/>
        public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => StreamAddAsync(key, streamField, streamValue, messageId, maxLength, useApproximateMaxLength, null, StreamTrimMode.KeepReferences, flags);

        /// <inheritdoc/>
        public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => StreamAdd(key, streamField, streamValue, RedisDatabase.LegacyStreamAddOptions(messageId, StreamIdempotentId.Empty, maxLength, useApproximateMaxLength, limit, trimMode), flags);

        /// <inheritdoc/>
        public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => StreamAddAsync(key, streamField, streamValue, RedisDatabase.LegacyStreamAddOptions(messageId, StreamIdempotentId.Empty, maxLength, useApproximateMaxLength, limit, trimMode), flags);

        /// <inheritdoc/>
        public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, StreamIdempotentId idempotentId, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => StreamAdd(key, streamField, streamValue, RedisDatabase.LegacyStreamAddOptions(null, in idempotentId, maxLength, useApproximateMaxLength, limit, trimMode), flags);

        /// <inheritdoc/>
        public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, StreamIdempotentId idempotentId, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => StreamAddAsync(key, streamField, streamValue, RedisDatabase.LegacyStreamAddOptions(null, in idempotentId, maxLength, useApproximateMaxLength, limit, trimMode), flags);

        /// <inheritdoc/>
        public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, StreamAddOptions options, CommandFlags flags = CommandFlags.None)
        {
            options.ThrowIfInvalid();
            return Wait(_inner.Streams.AddAsync(key, streamField, streamValue, in options, flags));
        }

        /// <inheritdoc/>
        public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, StreamAddOptions options, CommandFlags flags = CommandFlags.None)
        {
            options.ThrowIfInvalid();
            return _inner.Streams.AddAsync(key, streamField, streamValue, in options, flags).AsTask();
        }

        /// <inheritdoc/>
        public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => StreamAdd(key, streamPairs, messageId, maxLength, useApproximateMaxLength, null, StreamTrimMode.KeepReferences, flags);

        /// <inheritdoc/>
        public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => StreamAddAsync(key, streamPairs, messageId, maxLength, useApproximateMaxLength, null, StreamTrimMode.KeepReferences, flags);

        /// <inheritdoc/>
        public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => StreamAdd(key, streamPairs, RedisDatabase.LegacyStreamAddOptions(messageId, StreamIdempotentId.Empty, maxLength, useApproximateMaxLength, limit, trimMode), flags);

        /// <inheritdoc/>
        public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => StreamAddAsync(key, streamPairs, RedisDatabase.LegacyStreamAddOptions(messageId, StreamIdempotentId.Empty, maxLength, useApproximateMaxLength, limit, trimMode), flags);

        /// <inheritdoc/>
        public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, StreamIdempotentId idempotentId, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => StreamAdd(key, streamPairs, RedisDatabase.LegacyStreamAddOptions(null, in idempotentId, maxLength, useApproximateMaxLength, limit, trimMode), flags);

        /// <inheritdoc/>
        public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, StreamIdempotentId idempotentId, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None)
            => StreamAddAsync(key, streamPairs, RedisDatabase.LegacyStreamAddOptions(null, in idempotentId, maxLength, useApproximateMaxLength, limit, trimMode), flags);

        /// <inheritdoc/>
        public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, StreamAddOptions options, CommandFlags flags = CommandFlags.None)
        {
            options.ThrowIfInvalid();
            return Wait(_inner.Streams.AddAsync(key, Required(streamPairs, nameof(streamPairs)), in options, flags));
        }

        /// <inheritdoc/>
        public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, StreamAddOptions options, CommandFlags flags = CommandFlags.None)
        {
            options.ThrowIfInvalid();
            return _inner.Streams.AddAsync(key, Required(streamPairs, nameof(streamPairs)), in options, flags).AsTask();
        }

        // ---- XNACK ----------------------------------------------------------------------------------

        /// <inheritdoc/>
        public long StreamNegativeAcknowledge(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.NegativeAcknowledgeAsync(key, groupName, mode, messageId, flags));

        /// <inheritdoc/>
        public Task<long> StreamNegativeAcknowledgeAsync(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.NegativeAcknowledgeAsync(key, groupName, mode, messageId, flags).AsTask();

        /// <inheritdoc/>
        public long StreamNegativeAcknowledge(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.NegativeAcknowledgeAsync(key, groupName, mode, Required(messageIds, nameof(messageIds)), flags));

        /// <inheritdoc/>
        public Task<long> StreamNegativeAcknowledgeAsync(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.NegativeAcknowledgeAsync(key, groupName, mode, Required(messageIds, nameof(messageIds)), flags).AsTask();

        // ---- XACKDEL --------------------------------------------------------------------------------
        // The single-id overload has no counterpart on the new surface: XACKDEL is told IDS 1 and replies
        // with a one-element array either way, so a one-element span and [0] is the whole difference.

        /// <inheritdoc/>
        public StreamTrimResult StreamAcknowledgeAndDelete(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => Wait(SingleAcknowledgeAndDelete(key, groupName, mode, messageId, flags));

        /// <inheritdoc/>
        public Task<StreamTrimResult> StreamAcknowledgeAndDeleteAsync(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => SingleAcknowledgeAndDelete(key, groupName, mode, messageId, flags).AsTask();

        private async ValueTask<StreamTrimResult> SingleAcknowledgeAndDelete(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue messageId, CommandFlags flags)
        {
            // the span cannot be a local across the await, so the id is re-formed inside the lease's scope
            using var results = await _inner.Streams.AcknowledgeAndDeleteAsync(key, groupName, mode, new[] { messageId }, flags).ForAwait();
            return results.Length == 0 ? default : results.Span[0];
        }

        /// <inheritdoc/>
        public StreamTrimResult[] StreamAcknowledgeAndDelete(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.AcknowledgeAndDeleteArray(key, groupName, mode, Required(messageIds, nameof(messageIds)), flags));

        /// <inheritdoc/>
        public Task<StreamTrimResult[]> StreamAcknowledgeAndDeleteAsync(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.AcknowledgeAndDeleteArray(key, groupName, mode, Required(messageIds, nameof(messageIds)), flags).AsTask();

        // ---- XCFGSET --------------------------------------------------------------------------------

        /// <inheritdoc/>
        public void StreamConfigure(RedisKey key, StreamConfiguration configuration, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Streams.ConfigureAsync(key, configuration, flags));

        /// <inheritdoc/>
        public Task StreamConfigureAsync(RedisKey key, StreamConfiguration configuration, CommandFlags flags = CommandFlags.None)
            => _inner.Streams.ConfigureAsync(key, configuration, flags).AsTask();
    }
}
