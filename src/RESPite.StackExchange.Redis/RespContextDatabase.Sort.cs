using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    // Async Sort methods
    public Task<RedisValue[]> SortAsync(
        RedisKey key,
        long skip = 0,
        long take = -1,
        Order order = Order.Ascending,
        SortType sortType = SortType.Numeric,
        RedisValue by = default,
        RedisValue[]? get = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortAndStoreAsync(
        RedisKey destination,
        RedisKey key,
        long skip = 0,
        long take = -1,
        Order order = Order.Ascending,
        SortType sortType = SortType.Numeric,
        RedisValue by = default,
        RedisValue[]? get = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous Sort methods
    public RedisValue[] Sort(
        RedisKey key,
        long skip = 0,
        long take = -1,
        Order order = Order.Ascending,
        SortType sortType = SortType.Numeric,
        RedisValue by = default,
        RedisValue[]? get = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortAndStore(
        RedisKey destination,
        RedisKey key,
        long skip = 0,
        long take = -1,
        Order order = Order.Ascending,
        SortType sortType = SortType.Numeric,
        RedisValue by = default,
        RedisValue[]? get = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
