using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis.KeyspaceIsolation;

internal partial class KeyPrefixed<TInner>
{
    public Task<bool> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        Inner.ArraySetAsync(ToInner(key), index, value, flags);

    public Task<int> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        Inner.ArraySetAsync(ToInner(key), index, values, flags);

    public Task<int> ArraySetAsync(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None) =>
        Inner.ArraySetAsync(ToInner(key), values, flags);

    public Task<RedisValue> ArrayGetAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayGetAsync(ToInner(key), index, flags);

    public Task<RedisValue[]> ArrayGetAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayGetAsync(ToInner(key), indices, flags);

    public Task<RedisValue[]> ArrayGetRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayGetRangeAsync(ToInner(key), start, end, flags);

    public Task<RedisArrayIndex> ArrayLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayLengthAsync(ToInner(key), flags);

    public Task<RedisArrayIndex> ArrayCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayCountAsync(ToInner(key), flags);

    public Task<bool> ArrayDeleteAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayDeleteAsync(ToInner(key), index, flags);

    public Task<int> ArrayDeleteAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayDeleteAsync(ToInner(key), indices, flags);

    public Task<RedisArrayIndex> ArrayDeleteRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayDeleteRangeAsync(ToInner(key), start, end, flags);

    public Task<RedisArrayIndex> ArrayDeleteRangeAsync(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayDeleteRangeAsync(ToInner(key), ranges, flags);

    public Task<RedisArrayEntry[]> ArrayScanAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int limit = 0, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayScanAsync(ToInner(key), start, end, limit, flags);

    public Task<RedisArrayEntry[]> ArrayGrepAsync(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayGrepAsync(ToInner(key), request, flags);

    public Task<RedisValue> ArrayOperationAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayOperationAsync(ToInner(key), start, end, operation, operand, flags);

    public Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayRingAsync(ToInner(key), maxLength, value, flags);

    public Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, RedisArrayIndex maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayRingAsync(ToInner(key), maxLength, values, flags);

    public Task<RedisArrayIndex?> ArrayNextAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayNextAsync(ToInner(key), flags);

    public Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayInsertAsync(ToInner(key), value, flags);

    public Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayInsertAsync(ToInner(key), values, flags);

    public Task<bool> ArraySeekAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None) =>
        Inner.ArraySeekAsync(ToInner(key), index, flags);

    public Task<RedisValue[]> ArrayLastItemsAsync(RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayLastItemsAsync(ToInner(key), count, reverse, flags);

    public Task<ArrayInfo> ArrayInfoAsync(RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayInfoAsync(ToInner(key), full, flags);
}
