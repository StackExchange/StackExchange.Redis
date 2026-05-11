#pragma warning disable RS0026 // similar overloads

namespace StackExchange.Redis;

public partial interface IDatabase
{
    /// <summary>
    /// Sets the value at the specified array index.
    /// </summary>
    bool ArraySet(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Sets a contiguous range of array values starting at the specified index.
    /// </summary>
    long ArraySet(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Sets values at multiple array indices.
    /// </summary>
    long ArraySet(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the value at the specified array index.
    /// </summary>
    RedisValue ArrayGet(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the values at the specified array indices.
    /// </summary>
    RedisValue[] ArrayGet(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets values in the specified array index range.
    /// </summary>
    RedisValue[] ArrayGetRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the notional length of the array.
    /// </summary>
    long ArrayLength(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the number of array cells that have values.
    /// </summary>
    long ArrayCount(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Deletes the value at the specified array index.
    /// </summary>
    bool ArrayDelete(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Deletes values at the specified array indices.
    /// </summary>
    long ArrayDelete(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Deletes values in the specified array index range.
    /// </summary>
    long ArrayDeleteRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Deletes values in the specified array index ranges.
    /// </summary>
    long ArrayDeleteRange(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the non-empty values in the specified array index range.
    /// </summary>
    RedisArrayEntry[] ArrayScan(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, long limit = 0, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the array indices matching the specified grep request, optionally with values.
    /// </summary>
    /// <remarks>
    /// When <see cref="ArrayGrepRequest.IncludeValues"/> is <see langword="false"/>, returned entries contain only indices.
    /// When <see cref="ArrayGrepRequest.IncludeValues"/> is <see langword="true"/>, returned entries contain indices and values.
    /// </remarks>
    RedisArrayEntry[] ArrayGrep(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Performs an operation over the specified array range.
    /// </summary>
    RedisValue ArrayOperation(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Adds a value to a ring-buffer array.
    /// </summary>
    RedisArrayIndex ArrayRing(RedisKey key, long maxLength, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Adds values to a ring-buffer array.
    /// </summary>
    RedisArrayIndex ArrayRing(RedisKey key, long maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the current array write-head.
    /// </summary>
    RedisArrayIndex? ArrayNext(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Inserts a value at the current array write-head.
    /// </summary>
    RedisArrayIndex ArrayInsert(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Inserts values at the current array write-head.
    /// </summary>
    RedisArrayIndex ArrayInsert(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Moves the array write-head to the specified index.
    /// </summary>
    bool ArraySeek(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the previous array items.
    /// </summary>
    RedisValue[] ArrayLastItems(RedisKey key, long count, bool reverse = false, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets array metadata.
    /// </summary>
    ArrayInfo ArrayInfo(RedisKey key, CommandFlags flags = CommandFlags.None);
}

#pragma warning restore RS0026
