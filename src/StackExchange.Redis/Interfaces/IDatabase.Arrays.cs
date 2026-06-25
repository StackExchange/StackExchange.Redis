#pragma warning disable RS0026 // similar overloads

using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

public partial interface IDatabase
{
    /// <summary>
    /// Sets the value at the specified array index.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    bool ArraySet(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Sets a contiguous range of array values starting at the specified index.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    int ArraySet(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Sets values at multiple array indices.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    int ArraySet(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the value at the specified array index.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisValue ArrayGet(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the values at the specified array indices.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisValue[] ArrayGet(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets values in the specified array index range.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisValue[] ArrayGetRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the notional length of the array.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex ArrayLength(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the number of array cells that have values.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex ArrayCount(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Deletes the value at the specified array index.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    bool ArrayDelete(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Deletes values at the specified array indices.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    int ArrayDelete(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Deletes values in the specified array index range.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex ArrayDeleteRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Deletes values in the specified array index ranges.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex ArrayDeleteRange(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the non-empty values in the specified array index range.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayEntry[] ArrayScan(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int limit = 0, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the array indices matching the specified grep request, optionally with values.
    /// </summary>
    /// <remarks>
    /// When <see cref="ArrayGrepRequest.IncludeValues"/> is <see langword="false"/>, returned entries contain only indices.
    /// When <see cref="ArrayGrepRequest.IncludeValues"/> is <see langword="true"/>, returned entries contain indices and values.
    /// </remarks>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayEntry[] ArrayGrep(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Performs an operation over the specified array range.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisValue ArrayOperation(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Adds a value to a ring-buffer array.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex ArrayRing(RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Adds values to a ring-buffer array.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex ArrayRing(RedisKey key, RedisArrayIndex maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the current array write-head.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex? ArrayNext(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Inserts a value at the current array write-head.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex ArrayInsert(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Inserts values at the current array write-head.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisArrayIndex ArrayInsert(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Moves the array write-head to the specified index.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    bool ArraySeek(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets the previous array items.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    RedisValue[] ArrayLastItems(RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Gets array metadata.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    ArrayInfo ArrayInfo(RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None);
}

#pragma warning restore RS0026
