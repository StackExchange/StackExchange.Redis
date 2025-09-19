using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    // Async List methods
    public Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListInsertAfterAsync(
        RedisKey key,
        RedisValue pivot,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListInsertBeforeAsync(
        RedisKey key,
        RedisValue pivot,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> ListLeftPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<ListPopResult> ListLeftPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListPositionAsync(
        RedisKey key,
        RedisValue element,
        long rank = 1,
        long maxLength = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long[]> ListPositionsAsync(
        RedisKey key,
        RedisValue element,
        long count,
        long rank = 1,
        long maxLength = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListLeftPushAsync(
        RedisKey key,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) => when switch
    {
        When.Always => LPushAsync(key, value, flags),
        When.Exists => LPushXAsync(key, value, flags),
        _ => Task.FromResult(NotSupportedInt64(when)),
    };

    public Task<long> ListLeftPushAsync(
        RedisKey key,
        RedisValue[] values,
        When when,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> ListMoveAsync(
        RedisKey sourceKey,
        RedisKey destinationKey,
        ListSide sourceSide,
        ListSide destinationSide,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListRemoveAsync(
        RedisKey key,
        RedisValue value,
        long count = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> ListRightPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<ListPopResult> ListRightPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> ListRightPopLeftPushAsync(
        RedisKey source,
        RedisKey destination,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListRightPushAsync(
        RedisKey key,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) => when switch
    {
        When.Always => RPushAsync(key, value, flags),
        When.Exists => RPushXAsync(key, value, flags),
        _ => Task.FromResult(NotSupportedInt64(when)),
    };

    public Task<long> ListRightPushAsync(
        RedisKey key,
        RedisValue[] values,
        When when,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task ListSetByIndexAsync(
        RedisKey key,
        long index,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task ListTrimAsync(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous List methods
    public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ListInsertAfter(
        RedisKey key,
        RedisValue pivot,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ListInsertBefore(
        RedisKey key,
        RedisValue pivot,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    [RespCommand("lpop")]
    public partial RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None);

    public RedisValue[] ListLeftPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public ListPopResult ListLeftPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ListPosition(
        RedisKey key,
        RedisValue element,
        long rank = 1,
        long maxLength = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long[] ListPositions(
        RedisKey key,
        RedisValue element,
        long count,
        long rank = 1,
        long maxLength = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ListLeftPush(
        RedisKey key,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) => when switch
    {
        When.Always => LPush(key, value, flags),
        When.Exists => LPushX(key, value, flags),
        _ => NotSupportedInt64(when),
    };

    private static long NotSupportedInt64(When when) => throw new NotSupportedException(
        $"The condition '{when}' is not supported for this command");

    [RespCommand]
    private partial long LPush(RedisKey key, RedisValue value, CommandFlags flags);
    [RespCommand]
    private partial long LPushX(RedisKey key, RedisValue value, CommandFlags flags);

    public long ListLeftPush(
        RedisKey key,
        RedisValue[] values,
        When when,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue ListMove(
        RedisKey sourceKey,
        RedisKey destinationKey,
        ListSide sourceSide,
        ListSide destinationSide,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    [RespCommand("lrange")]
    public partial RedisValue[] ListRange(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None);

    public long ListRemove(
        RedisKey key,
        RedisValue value,
        long count = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    [RespCommand("rpop")]
    public partial RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None);

    public RedisValue[] ListRightPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public ListPopResult ListRightPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue ListRightPopLeftPush(
        RedisKey source,
        RedisKey destination,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ListRightPush(
        RedisKey key,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) => when switch
    {
        When.Always => RPush(key, value, flags),
        When.Exists => RPushX(key, value, flags),
        _ => NotSupportedInt64(when),
    };

    [RespCommand]
    private partial long RPush(RedisKey key, RedisValue value, CommandFlags flags);
    [RespCommand]
    private partial long RPushX(RedisKey key, RedisValue value, CommandFlags flags);

    public long ListRightPush(
        RedisKey key,
        RedisValue[] values,
        When when,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public void ListSetByIndex(
        RedisKey key,
        long index,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public void ListTrim(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
