using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The list commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ListRightPopLeftPush</c> is the interesting adapter here: the group has no method of its own for
    /// it, because <c>RPOPLPUSH</c> is exactly <c>LMOVE src dst RIGHT LEFT</c>. Naming both sides is the
    /// whole of the translation, which is the same trick <c>StringGetSet</c> plays against
    /// <c>SET ... GET</c>.
    /// </para>
    /// <para>
    /// <c>ListPopResult</c> is handed through unchanged for now: it exposes a <c>RedisValue[]</c>, so it is
    /// one of the three composite result types that keep an array inside an otherwise array-free surface.
    /// That is a shape decision rather than a mechanical one, and it is deliberately not being made here.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.GetByIndex(key, index, flags));

        /// <inheritdoc/>
        public Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
            => Context.Lists.GetByIndex(key, index, flags).AsTask();

        /// <inheritdoc/>
        public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.Length(key, flags));

        /// <inheritdoc/>
        public Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Lists.Length(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.RangeArray(key, start, stop, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
            => Context.Lists.RangeArray(key, start, stop, flags).AsTask();

        /// <inheritdoc/>
        public long ListPosition(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.Position(key, element, rank, maxLength, flags));

        /// <inheritdoc/>
        public Task<long> ListPositionAsync(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
            => Context.Lists.Position(key, element, rank, maxLength, flags).AsTask();

        /// <inheritdoc/>
        public long[] ListPositions(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.PositionsArray(key, element, count, rank, maxLength, flags));

        /// <inheritdoc/>
        public Task<long[]> ListPositionsAsync(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
            => Context.Lists.PositionsArray(key, element, count, rank, maxLength, flags).AsTask();

        // ---- pushes ------------------------------------------------------------------------------------
        // the CommandFlags-only overloads are the pre-`When` shapes; both are pure adapters

        /// <inheritdoc/>
        public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.LeftPush(key, value, when, flags));

        /// <inheritdoc/>
        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Context.Lists.LeftPush(key, value, when, flags).AsTask();

        /// <inheritdoc/>
        public long ListLeftPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.LeftPush(key, Required(values, nameof(values)), when, flags));

        /// <inheritdoc/>
        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Context.Lists.LeftPush(key, Required(values, nameof(values)), when, flags).AsTask();

        /// <inheritdoc/>
        public long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags)
            => ListLeftPush(key, values, When.Always, flags);

        /// <inheritdoc/>
        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
            => ListLeftPushAsync(key, values, When.Always, flags);

        /// <inheritdoc/>
        public long ListRightPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.RightPush(key, value, when, flags));

        /// <inheritdoc/>
        public Task<long> ListRightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Context.Lists.RightPush(key, value, when, flags).AsTask();

        /// <inheritdoc/>
        public long ListRightPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.RightPush(key, Required(values, nameof(values)), when, flags));

        /// <inheritdoc/>
        public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Context.Lists.RightPush(key, Required(values, nameof(values)), when, flags).AsTask();

        /// <inheritdoc/>
        public long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags)
            => ListRightPush(key, values, When.Always, flags);

        /// <inheritdoc/>
        public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
            => ListRightPushAsync(key, values, When.Always, flags);

        // ---- pops --------------------------------------------------------------------------------------

        /// <inheritdoc/>
        public RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.LeftPop(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Lists.LeftPop(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] ListLeftPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.LeftPopArray(key, count, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> ListLeftPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.Lists.LeftPopArray(key, count, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.RightPop(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Lists.RightPop(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] ListRightPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.RightPopArray(key, count, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> ListRightPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.Lists.RightPopArray(key, count, flags).AsTask();

        /// <inheritdoc/>
        public ListPopResult ListLeftPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.LeftPop(Required(keys, nameof(keys)), count, flags));

        /// <inheritdoc/>
        public Task<ListPopResult> ListLeftPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
            => Context.Lists.LeftPop(Required(keys, nameof(keys)), count, flags).AsTask();

        /// <inheritdoc/>
        public ListPopResult ListRightPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.RightPop(Required(keys, nameof(keys)), count, flags));

        /// <inheritdoc/>
        public Task<ListPopResult> ListRightPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
            => Context.Lists.RightPop(Required(keys, nameof(keys)), count, flags).AsTask();

        // ---- moves and edits ---------------------------------------------------------------------------

        /// <inheritdoc/>
        public RedisValue ListMove(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.Move(sourceKey, destinationKey, sourceSide, destinationSide, flags));

        /// <inheritdoc/>
        public Task<RedisValue> ListMoveAsync(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None)
            => Context.Lists.Move(sourceKey, destinationKey, sourceSide, destinationSide, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[]? ListMove(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, long count, ListMoveCount mode = ListMoveCount.UpTo, ListMoveOrder order = ListMoveOrder.Bulk, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.MoveArray(sourceKey, destinationKey, sourceSide, destinationSide, count, mode, order, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]?> ListMoveAsync(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, long count, ListMoveCount mode = ListMoveCount.UpTo, ListMoveOrder order = ListMoveOrder.Bulk, CommandFlags flags = CommandFlags.None)
            => Context.Lists.MoveArray(sourceKey, destinationKey, sourceSide, destinationSide, count, mode, order, flags).AsTask();

        /// <inheritdoc/>
        /// <remarks>
        /// <c>RPOPLPUSH</c> is <c>LMOVE src dst RIGHT LEFT</c>, deprecated in its favour since 6.2; naming
        /// the two sides is the whole of the translation, so callers of the old name get <c>LMOVE</c>
        /// wherever the server has it and <c>RPOPLPUSH</c> wherever it might not. See
        /// <c>IRespServerFeatures</c>.
        /// </remarks>
        public RedisValue ListRightPopLeftPush(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.RightPopLeftPush(source, destination, flags));

        /// <inheritdoc cref="ListRightPopLeftPush"/>
        public Task<RedisValue> ListRightPopLeftPushAsync(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
            => Context.Lists.RightPopLeftPush(source, destination, flags).AsTask();

        /// <inheritdoc/>
        public long ListInsertBefore(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.InsertBefore(key, pivot, value, flags));

        /// <inheritdoc/>
        public Task<long> ListInsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Lists.InsertBefore(key, pivot, value, flags).AsTask();

        /// <inheritdoc/>
        public long ListInsertAfter(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.InsertAfter(key, pivot, value, flags));

        /// <inheritdoc/>
        public Task<long> ListInsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Lists.InsertAfter(key, pivot, value, flags).AsTask();

        /// <inheritdoc/>
        public long ListRemove(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.Remove(key, value, count, flags));

        /// <inheritdoc/>
        public Task<long> ListRemoveAsync(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
            => Context.Lists.Remove(key, value, count, flags).AsTask();

        /// <inheritdoc/>
        public void ListSetByIndex(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.SetByIndex(key, index, value, flags));

        /// <inheritdoc/>
        public Task ListSetByIndexAsync(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Lists.SetByIndex(key, index, value, flags).AsTask();

        /// <inheritdoc/>
        public void ListTrim(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Lists.Trim(key, start, stop, flags));

        /// <inheritdoc/>
        public Task ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
            => Context.Lists.Trim(key, start, stop, flags).AsTask();
    }
}
