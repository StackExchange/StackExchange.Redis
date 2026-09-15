using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The array commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// The whole family except <c>ARGREP</c>: <see cref="ArrayGrepRequest"/> is a builder that renders its
    /// own predicates through the old <c>MessageWriter</c>, so moving it is a decision about that type.
    /// Arrays become spans here, which is the adapter's job and not the new surface's.
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public bool ArraySet(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.SetAsync(key, index, value, flags));

        /// <inheritdoc/>
        public Task<bool> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.SetAsync(key, index, value, flags).AsTask();

        /// <inheritdoc/>
        public int ArraySet(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => checked((int)Wait(Context.Arrays.SetAsync(key, index, Required(values, nameof(values)), flags)));

        /// <inheritdoc/>
        public async Task<int> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => checked((int)await Context.Arrays.SetAsync(key, index, Required(values, nameof(values)), flags).ForAwait());

        /// <inheritdoc/>
        public int ArraySet(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None)
            => checked((int)Wait(Context.Arrays.SetAsync(key, Required(values, nameof(values)), flags)));

        /// <inheritdoc/>
        public async Task<int> ArraySetAsync(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None)
            => checked((int)await Context.Arrays.SetAsync(key, Required(values, nameof(values)), flags).ForAwait());

        /// <inheritdoc/>
        public RedisValue ArrayGet(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.GetAsync(key, index, flags));

        /// <inheritdoc/>
        public Task<RedisValue> ArrayGetAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.GetAsync(key, index, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] ArrayGet(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.GetArray(key, Required(indices, nameof(indices)), flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> ArrayGetAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.GetArray(key, Required(indices, nameof(indices)), flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] ArrayGetRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.GetRangeArray(key, start, end, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> ArrayGetRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.GetRangeArray(key, start, end, flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayIndex ArrayLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex> ArrayLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.LengthAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayIndex ArrayCount(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.CountAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex> ArrayCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.CountAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public bool ArrayDelete(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.DeleteAsync(key, index, flags));

        /// <inheritdoc/>
        public Task<bool> ArrayDeleteAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.DeleteAsync(key, index, flags).AsTask();

        /// <inheritdoc/>
        public int ArrayDelete(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
            => checked((int)Wait(Context.Arrays.DeleteAsync(key, Required(indices, nameof(indices)), flags)));

        /// <inheritdoc/>
        public async Task<int> ArrayDeleteAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
            => checked((int)await Context.Arrays.DeleteAsync(key, Required(indices, nameof(indices)), flags).ForAwait());

        /// <inheritdoc/>
        public RedisArrayIndex ArrayDeleteRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.DeleteRangeAsync(key, start, end, flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex> ArrayDeleteRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.DeleteRangeAsync(key, start, end, flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayIndex ArrayDeleteRange(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.DeleteRangeAsync(key, Required(ranges, nameof(ranges)), flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex> ArrayDeleteRangeAsync(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.DeleteRangeAsync(key, Required(ranges, nameof(ranges)), flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayEntry[] ArrayScan(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int limit = 0, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.ScanArray(key, start, end, limit, flags));

        /// <inheritdoc/>
        public Task<RedisArrayEntry[]> ArrayScanAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int limit = 0, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.ScanArray(key, start, end, limit, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue ArrayOperation(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.OperationAsync(key, start, end, operation, operand, flags));

        /// <inheritdoc/>
        public Task<RedisValue> ArrayOperationAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.OperationAsync(key, start, end, operation, operand, flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayIndex ArrayRing(RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.RingAsync(key, maxLength, value, flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.RingAsync(key, maxLength, value, flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayIndex ArrayRing(RedisKey key, RedisArrayIndex maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.RingAsync(key, maxLength, Required(values, nameof(values)), flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, RedisArrayIndex maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.RingAsync(key, maxLength, Required(values, nameof(values)), flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayIndex? ArrayNext(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.NextAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex?> ArrayNextAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.NextAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayIndex ArrayInsert(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.InsertAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.InsertAsync(key, value, flags).AsTask();

        /// <inheritdoc/>
        public RedisArrayIndex ArrayInsert(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.InsertAsync(key, Required(values, nameof(values)), flags));

        /// <inheritdoc/>
        public Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.InsertAsync(key, Required(values, nameof(values)), flags).AsTask();

        /// <inheritdoc/>
        public bool ArraySeek(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.SeekAsync(key, index, flags));

        /// <inheritdoc/>
        public Task<bool> ArraySeekAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.SeekAsync(key, index, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] ArrayLastItems(RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.LastItemsArray(key, count, reverse, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> ArrayLastItemsAsync(RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.LastItemsArray(key, count, reverse, flags).AsTask();

        /// <inheritdoc/>
        public ArrayInfo ArrayInfo(RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Arrays.InfoAsync(key, full, flags));

        /// <inheritdoc/>
        public Task<ArrayInfo> ArrayInfoAsync(RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None)
            => Context.Arrays.InfoAsync(key, full, flags).AsTask();
    }
}
