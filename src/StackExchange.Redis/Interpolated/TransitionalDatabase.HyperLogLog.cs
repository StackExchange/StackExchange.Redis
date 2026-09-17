using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// The HyperLogLog commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// The old surface spells every one of these twice - a fixed-arity form and an array form - and the
    /// group collapses both onto the span, so the pairs meet again here. <c>HyperLogLogMerge</c> is the
    /// one that loses a shape rather than a spelling: the (first, second) overload is two keys, and two
    /// keys are a span.
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public bool HyperLogLogAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.AddAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.AddAsync(key, value, flags).AsTask();

        /// <inheritdoc/>
        public bool HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.AddAsync(key, Required(values, nameof(values)), flags));

        /// <inheritdoc/>
        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.AddAsync(key, Required(values, nameof(values)), flags).AsTask();

        /// <inheritdoc/>
        public long HyperLogLogLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.LengthAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.LengthAsync(Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.LengthAsync(Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public void HyperLogLogMerge(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.MergeAsync(destination, [first, second], flags));

        /// <inheritdoc/>
        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.MergeAsync(destination, [first, second], flags).AsTask();

        /// <inheritdoc/>
        public void HyperLogLogMerge(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.MergeAsync(destination, Required(sourceKeys, nameof(sourceKeys)), flags));

        /// <inheritdoc/>
        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.MergeAsync(destination, Required(sourceKeys, nameof(sourceKeys)), flags).AsTask();
    }
}
