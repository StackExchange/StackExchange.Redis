using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Array Async operations
    public Task<bool> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArraySetAsync(key, index, value, flags);

    public Task<int> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArraySetAsync(key, index, values, flags);

    public Task<int> ArraySetAsync(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArraySetAsync(key, values, flags);

    public Task<RedisValue> ArrayGetAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayGetAsync(key, index, flags);

    public Task<RedisValue[]> ArrayGetAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayGetAsync(key, indices, flags);

    public Task<RedisValue[]> ArrayGetRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayGetRangeAsync(key, start, end, flags);

    public Task<RedisArrayIndex> ArrayLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayLengthAsync(key, flags);

    public Task<RedisArrayIndex> ArrayCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayCountAsync(key, flags);

    public Task<bool> ArrayDeleteAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayDeleteAsync(key, index, flags);

    public Task<int> ArrayDeleteAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayDeleteAsync(key, indices, flags);

    public Task<RedisArrayIndex> ArrayDeleteRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayDeleteRangeAsync(key, start, end, flags);

    public Task<RedisArrayIndex> ArrayDeleteRangeAsync(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayDeleteRangeAsync(key, ranges, flags);

    public Task<RedisArrayEntry[]> ArrayScanAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int limit = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayScanAsync(key, start, end, limit, flags);

    public Task<RedisArrayEntry[]> ArrayGrepAsync(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayGrepAsync(key, request, flags);

    public Task<RedisValue> ArrayOperationAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayOperationAsync(key, start, end, operation, operand, flags);

    public Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayRingAsync(key, maxLength, value, flags);

    public Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, RedisArrayIndex maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayRingAsync(key, maxLength, values, flags);

    public Task<RedisArrayIndex?> ArrayNextAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayNextAsync(key, flags);

    public Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayInsertAsync(key, value, flags);

    public Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayInsertAsync(key, values, flags);

    public Task<bool> ArraySeekAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArraySeekAsync(key, index, flags);

    public Task<RedisValue[]> ArrayLastItemsAsync(RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayLastItemsAsync(key, count, reverse, flags);

    public Task<ArrayInfo> ArrayInfoAsync(RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayInfoAsync(key, full, flags);
}
