using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LIndex(key, index).Wait(SyncTimeout);

    public Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LIndex(key, index).AsTask();

    public long ListInsertAfter(
        RedisKey key,
        RedisValue pivot,
        RedisValue value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LInsert(key, false, pivot, value).Wait(SyncTimeout);

    public Task<long> ListInsertAfterAsync(
        RedisKey key,
        RedisValue pivot,
        RedisValue value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LInsert(key, false, pivot, value).AsTask();

    public long ListInsertBefore(
        RedisKey key,
        RedisValue pivot,
        RedisValue value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LInsert(key, true, pivot, value).Wait(SyncTimeout);

    public Task<long> ListInsertBeforeAsync(
        RedisKey key,
        RedisValue pivot,
        RedisValue value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LInsert(key, true, pivot, value).AsTask();

    public RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPop(key).Wait(SyncTimeout);

    public RedisValue[] ListLeftPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPop(key, count).Wait(SyncTimeout);

    public ListPopResult ListLeftPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LMPop(keys, ListSide.Left, count).Wait(SyncTimeout);

    public Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPop(key).AsTask();

    public Task<RedisValue[]> ListLeftPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPop(key, count).AsTask();

    public Task<ListPopResult> ListLeftPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LMPop(keys, ListSide.Left, count).AsTask();

    public long ListLeftPush(
        RedisKey key,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().Push(key, value, ListSide.Left, when).Wait(SyncTimeout);

    public long ListLeftPush(
        RedisKey key,
        RedisValue[] values,
        When when,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().Push(key, values, ListSide.Left, when).Wait(SyncTimeout);

    public long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPush(key, values).Wait(SyncTimeout);

    public Task<long> ListLeftPushAsync(
        RedisKey key,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().Push(key, value, ListSide.Left, when).AsTask();

    public Task<long> ListLeftPushAsync(
        RedisKey key,
        RedisValue[] values,
        When when,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().Push(key, values, ListSide.Left, when).AsTask();

    public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPush(key, values).AsTask();

    public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LLen(key).Wait(SyncTimeout);

    public Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LLen(key).AsTask();

    public RedisValue ListMove(
        RedisKey sourceKey,
        RedisKey destinationKey,
        ListSide sourceSide,
        ListSide destinationSide,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LMove(sourceKey, destinationKey, sourceSide, destinationSide).Wait(SyncTimeout);

    public Task<RedisValue> ListMoveAsync(
        RedisKey sourceKey,
        RedisKey destinationKey,
        ListSide sourceSide,
        ListSide destinationSide,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LMove(sourceKey, destinationKey, sourceSide, destinationSide).AsTask();

    public long ListPosition(
        RedisKey key,
        RedisValue element,
        long rank = 1,
        long maxLength = 0,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPos(key, element, rank, maxLength).Wait(SyncTimeout);

    public Task<long> ListPositionAsync(
        RedisKey key,
        RedisValue element,
        long rank = 1,
        long maxLength = 0,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPos(key, element, rank, maxLength).AsTask();

    public long[] ListPositions(
        RedisKey key,
        RedisValue element,
        long count,
        long rank = 1,
        long maxLength = 0,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPos(key, element, rank, maxLength, count).Wait(SyncTimeout);

    public Task<long[]> ListPositionsAsync(
        RedisKey key,
        RedisValue element,
        long count,
        long rank = 1,
        long maxLength = 0,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LPos(key, element, rank, maxLength, count).AsTask();

    public RedisValue[] ListRange(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LRange(key, start, stop).Wait(SyncTimeout);

    public Task<RedisValue[]> ListRangeAsync(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LRange(key, start, stop).AsTask();

    public long ListRemove(
        RedisKey key,
        RedisValue value,
        long count = 0,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LRem(key, count, value).Wait(SyncTimeout);

    public Task<long> ListRemoveAsync(
        RedisKey key,
        RedisValue value,
        long count = 0,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LRem(key, count, value).AsTask();

    public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().RPop(key).Wait(SyncTimeout);

    public RedisValue[] ListRightPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().RPop(key, count).Wait(SyncTimeout);

    public ListPopResult ListRightPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LMPop(keys, ListSide.Right, count).Wait(SyncTimeout);

    public Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().RPop(key).AsTask();

    public Task<RedisValue[]> ListRightPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().RPop(key, count).AsTask();

    public Task<ListPopResult> ListRightPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LMPop(keys, ListSide.Right, count).AsTask();

    public RedisValue ListRightPopLeftPush(
        RedisKey source,
        RedisKey destination,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().RPopLPush(source, destination).Wait(SyncTimeout);

    public Task<RedisValue> ListRightPopLeftPushAsync(
        RedisKey source,
        RedisKey destination,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().RPopLPush(source, destination).AsTask();

    public long ListRightPush(
        RedisKey key,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().Push(key, value, ListSide.Right, when).Wait(SyncTimeout);

    public long ListRightPush(
        RedisKey key,
        RedisValue[] values,
        When when,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().Push(key, values, ListSide.Right, when).Wait(SyncTimeout);

    public long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().RPush(key, values).Wait(SyncTimeout);

    public Task<long> ListRightPushAsync(
        RedisKey key,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().Push(key, value, ListSide.Right, when).AsTask();

    public Task<long> ListRightPushAsync(
        RedisKey key,
        RedisValue[] values,
        When when,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().Push(key, values, ListSide.Right, when).AsTask();

    public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().RPush(key, values).AsTask();

    public void ListSetByIndex(
        RedisKey key,
        long index,
        RedisValue value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LSet(key, index, value).Wait(SyncTimeout);

    public Task ListSetByIndexAsync(
        RedisKey key,
        long index,
        RedisValue value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LSet(key, index, value).AsTask();

    public void ListTrim(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LTrim(key, start, stop).Wait(SyncTimeout);

    public Task ListTrimAsync(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Lists().LTrim(key, start, stop).AsTask();
}
