#pragma warning disable RS0026 // similar overloads

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis;

public partial interface IDatabaseAsync
{
    /// <inheritdoc cref="IDatabase.ArraySet(RedisKey, RedisArrayIndex, RedisValue, CommandFlags)"/>
    Task<bool> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArraySet(RedisKey, RedisArrayIndex, RedisValue[], CommandFlags)"/>
    Task<int> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArraySet(RedisKey, RedisArrayEntry[], CommandFlags)"/>
    Task<int> ArraySetAsync(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayGet(RedisKey, RedisArrayIndex, CommandFlags)"/>
    Task<RedisValue> ArrayGetAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayGet(RedisKey, RedisArrayIndex[], CommandFlags)"/>
    Task<RedisValue[]> ArrayGetAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayGetRange(RedisKey, RedisArrayIndex, RedisArrayIndex, CommandFlags)"/>
    Task<RedisValue[]> ArrayGetRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayLength(RedisKey, CommandFlags)"/>
    Task<RedisArrayIndex> ArrayLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayCount(RedisKey, CommandFlags)"/>
    Task<RedisArrayIndex> ArrayCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayDelete(RedisKey, RedisArrayIndex, CommandFlags)"/>
    Task<bool> ArrayDeleteAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayDelete(RedisKey, RedisArrayIndex[], CommandFlags)"/>
    Task<int> ArrayDeleteAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayDeleteRange(RedisKey, RedisArrayIndex, RedisArrayIndex, CommandFlags)"/>
    Task<RedisArrayIndex> ArrayDeleteRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayDeleteRange(RedisKey, RedisArrayRange[], CommandFlags)"/>
    Task<RedisArrayIndex> ArrayDeleteRangeAsync(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayScan(RedisKey, RedisArrayIndex, RedisArrayIndex, int, CommandFlags)"/>
    Task<RedisArrayEntry[]> ArrayScanAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int limit = 0, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayGrep(RedisKey, ArrayGrepRequest, CommandFlags)"/>
    Task<RedisArrayEntry[]> ArrayGrepAsync(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayOperation(RedisKey, RedisArrayIndex, RedisArrayIndex, ArrayOperation, RedisValue, CommandFlags)"/>
    Task<RedisValue> ArrayOperationAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayRing(RedisKey, RedisArrayIndex, RedisValue, CommandFlags)"/>
    Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayRing(RedisKey, RedisArrayIndex, RedisValue[], CommandFlags)"/>
    Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, RedisArrayIndex maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayNext(RedisKey, CommandFlags)"/>
    Task<RedisArrayIndex?> ArrayNextAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayInsert(RedisKey, RedisValue, CommandFlags)"/>
    Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayInsert(RedisKey, RedisValue[], CommandFlags)"/>
    Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArraySeek(RedisKey, RedisArrayIndex, CommandFlags)"/>
    Task<bool> ArraySeekAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayLastItems(RedisKey, int, bool, CommandFlags)"/>
    Task<RedisValue[]> ArrayLastItemsAsync(RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.ArrayInfo(RedisKey, bool, CommandFlags)"/>
    Task<ArrayInfo> ArrayInfoAsync(RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None);
}

#pragma warning restore RS0026
