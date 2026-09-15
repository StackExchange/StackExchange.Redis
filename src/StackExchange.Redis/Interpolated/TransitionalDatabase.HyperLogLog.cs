using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
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
            => Wait(Context.HyperLogLog.Add(key, value, flags));

        /// <inheritdoc/>
        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.Add(key, value, flags).AsTask();

        /// <inheritdoc/>
        public bool HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.Add(key, Required(values, nameof(values)), flags));

        /// <inheritdoc/>
        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.Add(key, Required(values, nameof(values)), flags).AsTask();

        /// <inheritdoc/>
        public long HyperLogLogLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.Length(key, flags));

        /// <inheritdoc/>
        public Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.Length(key, flags).AsTask();

        /// <inheritdoc/>
        public long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.Length(Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.Length(Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public void HyperLogLogMerge(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.Merge(destination, [first, second], flags));

        /// <inheritdoc/>
        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.Merge(destination, [first, second], flags).AsTask();

        /// <inheritdoc/>
        public void HyperLogLogMerge(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.HyperLogLog.Merge(destination, Required(sourceKeys, nameof(sourceKeys)), flags));

        /// <inheritdoc/>
        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
            => Context.HyperLogLog.Merge(destination, Required(sourceKeys, nameof(sourceKeys)), flags).AsTask();
    }
}
