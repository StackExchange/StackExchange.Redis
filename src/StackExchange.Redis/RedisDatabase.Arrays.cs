using System;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    public bool ArraySet(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARSET, key, index.ToRedisValue(), value);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public long ArraySet(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArraySetMessage(key, index, values, flags);
        return msg is null ? 0 : ExecuteSync(msg, ResultProcessor.Int64);
    }

    public long ArraySet(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArraySetMessage(key, values, flags);
        return msg is null ? 0 : ExecuteSync(msg, ResultProcessor.Int64);
    }

    public RedisValue ArrayGet(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARGET, key, index.ToRedisValue());
        return ExecuteSync(msg, ResultProcessor.RedisValue);
    }

    public RedisValue[] ArrayGet(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayIndicesMessage(RedisCommand.ARMGET, key, indices, flags);
        return msg is null ? Array.Empty<RedisValue>() : ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
    }

    public RedisValue[] ArrayGetRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARGETRANGE, key, start.ToRedisValue(), end.ToRedisValue());
        return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
    }

    public long ArrayLength(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARLEN, key);
        return ExecuteSync(msg, ResultProcessor.Int64);
    }

    public long ArrayCount(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARCOUNT, key);
        return ExecuteSync(msg, ResultProcessor.Int64);
    }

    public bool ArrayDelete(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARDEL, key, index.ToRedisValue());
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public long ArrayDelete(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayIndicesMessage(RedisCommand.ARDEL, key, indices, flags);
        return msg is null ? 0 : ExecuteSync(msg, ResultProcessor.Int64);
    }

    public long ArrayDeleteRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARDELRANGE, key, start.ToRedisValue(), end.ToRedisValue());
        return ExecuteSync(msg, ResultProcessor.Int64);
    }

    public long ArrayDeleteRange(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayRangesMessage(key, ranges, flags);
        return msg is null ? 0 : ExecuteSync(msg, ResultProcessor.Int64);
    }

    public RedisArrayEntry[] ArrayScan(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, long limit = 0, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayScanMessage(key, start, end, limit, flags);
        return ExecuteSync(msg, ResultProcessor.RedisArrayEntryArray, defaultValue: Array.Empty<RedisArrayEntry>());
    }

    public RedisArrayEntry[] ArrayGrep(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var msg = request.CreateMessage(Database, key, flags);
        var processor = request.IncludeValues ? ResultProcessor.RedisArrayEntryArray : ResultProcessor.RedisArrayIndexEntryArray;
        return ExecuteSync(msg, processor, defaultValue: Array.Empty<RedisArrayEntry>());
    }

    public RedisValue ArrayOperation(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayOperationMessage(key, start, end, operation, operand, flags);
        return ExecuteSync(msg, ResultProcessor.RedisValue);
    }

    public RedisArrayIndex ArrayRing(RedisKey key, long maxLength, RedisValue value, CommandFlags flags = CommandFlags.None)
    {
        CheckNonNegative(maxLength, nameof(maxLength));
        var msg = Message.Create(Database, flags, RedisCommand.ARRING, key, maxLength, value);
        return ExecuteSync(msg, ResultProcessor.RedisArrayIndex);
    }

    public RedisArrayIndex ArrayRing(RedisKey key, long maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayRingMessage(key, maxLength, values, flags);
        return ExecuteSync(msg, ResultProcessor.RedisArrayIndex);
    }

    public RedisArrayIndex? ArrayNext(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARNEXT, key);
        return ExecuteSync(msg, ResultProcessor.NullableRedisArrayIndex);
    }

    public RedisArrayIndex ArrayInsert(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARINSERT, key, value);
        return ExecuteSync(msg, ResultProcessor.RedisArrayIndex);
    }

    public RedisArrayIndex ArrayInsert(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayValuesMessage(RedisCommand.ARINSERT, key, values, flags);
        return ExecuteSync(msg, ResultProcessor.RedisArrayIndex);
    }

    public bool ArraySeek(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARSEEK, key, index.ToRedisValue());
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public RedisValue[] ArrayLastItems(RedisKey key, long count, bool reverse = false, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayLastItemsMessage(key, count, reverse, flags);
        return msg is null ? Array.Empty<RedisValue>() : ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
    }

    public ArrayInfo ArrayInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARINFO, key);
        return ExecuteSync(msg, ResultProcessor.ArrayInfo);
    }

    public Task<bool> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARSET, key, index.ToRedisValue(), value);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public Task<long> ArraySetAsync(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArraySetMessage(key, index, values, flags);
        return msg is null ? CompletedTask<long>.FromDefault(0, asyncState) : ExecuteAsync(msg, ResultProcessor.Int64);
    }

    public Task<long> ArraySetAsync(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArraySetMessage(key, values, flags);
        return msg is null ? CompletedTask<long>.FromDefault(0, asyncState) : ExecuteAsync(msg, ResultProcessor.Int64);
    }

    public Task<RedisValue> ArrayGetAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARGET, key, index.ToRedisValue());
        return ExecuteAsync(msg, ResultProcessor.RedisValue);
    }

    public Task<RedisValue[]> ArrayGetAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayIndicesMessage(RedisCommand.ARMGET, key, indices, flags);
        return msg is null
            ? CompletedTask<RedisValue[]>.FromDefault(Array.Empty<RedisValue>(), asyncState)
            : ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
    }

    public Task<RedisValue[]> ArrayGetRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARGETRANGE, key, start.ToRedisValue(), end.ToRedisValue());
        return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
    }

    public Task<long> ArrayLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARLEN, key);
        return ExecuteAsync(msg, ResultProcessor.Int64);
    }

    public Task<long> ArrayCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARCOUNT, key);
        return ExecuteAsync(msg, ResultProcessor.Int64);
    }

    public Task<bool> ArrayDeleteAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARDEL, key, index.ToRedisValue());
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public Task<long> ArrayDeleteAsync(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayIndicesMessage(RedisCommand.ARDEL, key, indices, flags);
        return msg is null ? CompletedTask<long>.FromDefault(0, asyncState) : ExecuteAsync(msg, ResultProcessor.Int64);
    }

    public Task<long> ArrayDeleteRangeAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARDELRANGE, key, start.ToRedisValue(), end.ToRedisValue());
        return ExecuteAsync(msg, ResultProcessor.Int64);
    }

    public Task<long> ArrayDeleteRangeAsync(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayRangesMessage(key, ranges, flags);
        return msg is null ? CompletedTask<long>.FromDefault(0, asyncState) : ExecuteAsync(msg, ResultProcessor.Int64);
    }

    public Task<RedisArrayEntry[]> ArrayScanAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, long limit = 0, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayScanMessage(key, start, end, limit, flags);
        return ExecuteAsync(msg, ResultProcessor.RedisArrayEntryArray, defaultValue: Array.Empty<RedisArrayEntry>());
    }

    public Task<RedisArrayEntry[]> ArrayGrepAsync(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var msg = request.CreateMessage(Database, key, flags);
        var processor = request.IncludeValues ? ResultProcessor.RedisArrayEntryArray : ResultProcessor.RedisArrayIndexEntryArray;
        return ExecuteAsync(msg, processor, defaultValue: Array.Empty<RedisArrayEntry>());
    }

    public Task<RedisValue> ArrayOperationAsync(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayOperationMessage(key, start, end, operation, operand, flags);
        return ExecuteAsync(msg, ResultProcessor.RedisValue);
    }

    public Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, long maxLength, RedisValue value, CommandFlags flags = CommandFlags.None)
    {
        CheckNonNegative(maxLength, nameof(maxLength));
        var msg = Message.Create(Database, flags, RedisCommand.ARRING, key, maxLength, value);
        return ExecuteAsync(msg, ResultProcessor.RedisArrayIndex);
    }

    public Task<RedisArrayIndex> ArrayRingAsync(RedisKey key, long maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayRingMessage(key, maxLength, values, flags);
        return ExecuteAsync(msg, ResultProcessor.RedisArrayIndex);
    }

    public Task<RedisArrayIndex?> ArrayNextAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARNEXT, key);
        return ExecuteAsync(msg, ResultProcessor.NullableRedisArrayIndex);
    }

    public Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARINSERT, key, value);
        return ExecuteAsync(msg, ResultProcessor.RedisArrayIndex);
    }

    public Task<RedisArrayIndex> ArrayInsertAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayValuesMessage(RedisCommand.ARINSERT, key, values, flags);
        return ExecuteAsync(msg, ResultProcessor.RedisArrayIndex);
    }

    public Task<bool> ArraySeekAsync(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARSEEK, key, index.ToRedisValue());
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public Task<RedisValue[]> ArrayLastItemsAsync(RedisKey key, long count, bool reverse = false, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetArrayLastItemsMessage(key, count, reverse, flags);
        return msg is null
            ? CompletedTask<RedisValue[]>.FromDefault(Array.Empty<RedisValue>(), asyncState)
            : ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
    }

    public Task<ArrayInfo> ArrayInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.ARINFO, key);
        return ExecuteAsync(msg, ResultProcessor.ArrayInfo);
    }

    private static void CheckNonNegative(long value, string parameterName)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(parameterName, "The value must be non-negative.");
    }

    private Message? GetArraySetMessage(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        if (values.Length == 0) return null;

        var args = new RedisValue[values.Length + 1];
        args[0] = index.ToRedisValue();
        Array.Copy(values, 0, args, 1, values.Length);
        return Message.Create(Database, flags, RedisCommand.ARSET, key, args);
    }

    private Message? GetArraySetMessage(RedisKey key, RedisArrayEntry[] values, CommandFlags flags)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        if (values.Length == 0) return null;

        var args = new RedisValue[values.Length * 2];
        int offset = 0;
        foreach (var value in values)
        {
            args[offset++] = value.index.ToRedisValue();
            args[offset++] = value.value;
        }
        return Message.Create(Database, flags, RedisCommand.ARMSET, key, args);
    }

    private Message? GetArrayIndicesMessage(RedisCommand command, RedisKey key, RedisArrayIndex[] indices, CommandFlags flags)
    {
        if (indices == null) throw new ArgumentNullException(nameof(indices));
        if (indices.Length == 0) return null;

        var args = new RedisValue[indices.Length];
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = indices[i].ToRedisValue();
        }
        return Message.Create(Database, flags, command, key, args);
    }

    private Message? GetArrayRangesMessage(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags)
    {
        if (ranges == null) throw new ArgumentNullException(nameof(ranges));
        if (ranges.Length == 0) return null;

        var args = new RedisValue[ranges.Length * 2];
        int offset = 0;
        foreach (var range in ranges)
        {
            args[offset++] = range.start.ToRedisValue();
            args[offset++] = range.end.ToRedisValue();
        }
        return Message.Create(Database, flags, RedisCommand.ARDELRANGE, key, args);
    }

    private Message GetArrayScanMessage(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, long limit, CommandFlags flags)
    {
        CheckNonNegative(limit, nameof(limit));
        return limit == 0
            ? Message.Create(Database, flags, RedisCommand.ARSCAN, key, start.ToRedisValue(), end.ToRedisValue())
            : Message.Create(Database, flags, RedisCommand.ARSCAN, key, start.ToRedisValue(), end.ToRedisValue(), RedisLiterals.LIMIT, limit);
    }

    private Message GetArrayOperationMessage(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand, CommandFlags flags)
    {
        bool hasOperand = !operand.IsNull;
        if (operation == global::StackExchange.Redis.ArrayOperation.Match)
        {
            if (!hasOperand)
            {
                throw new ArgumentException("The Match operation requires a non-null operand.", nameof(operand));
            }
        }
        else if (hasOperand)
        {
            throw new ArgumentException("An operand is only supported for the Match operation.", nameof(operand));
        }

        var literal = GetArrayOperationLiteral(operation);
        return hasOperand
            ? Message.Create(Database, flags, RedisCommand.AROP, key, start.ToRedisValue(), end.ToRedisValue(), literal, operand)
            : Message.Create(Database, flags, RedisCommand.AROP, key, start.ToRedisValue(), end.ToRedisValue(), literal);
    }

    private static RedisValue GetArrayOperationLiteral(ArrayOperation operation) => operation switch
    {
        global::StackExchange.Redis.ArrayOperation.Sum => RedisLiterals.SUM,
        global::StackExchange.Redis.ArrayOperation.Min => RedisLiterals.MIN,
        global::StackExchange.Redis.ArrayOperation.Max => RedisLiterals.MAX,
        global::StackExchange.Redis.ArrayOperation.And => RedisLiterals.AND,
        global::StackExchange.Redis.ArrayOperation.Or => RedisLiterals.OR,
        global::StackExchange.Redis.ArrayOperation.Xor => RedisLiterals.XOR,
        global::StackExchange.Redis.ArrayOperation.Match => RedisLiterals.MATCH,
        global::StackExchange.Redis.ArrayOperation.Used => RedisLiterals.USED,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private Message GetArrayRingMessage(RedisKey key, long maxLength, RedisValue[] values, CommandFlags flags)
    {
        CheckNonNegative(maxLength, nameof(maxLength));
        return GetArrayValuesMessage(RedisCommand.ARRING, key, values, flags, maxLength);
    }

    private Message GetArrayValuesMessage(RedisCommand command, RedisKey key, RedisValue[] values, CommandFlags flags, RedisValue? prefix = null)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        if (values.Length == 0) throw new ArgumentOutOfRangeException(nameof(values));

        if (prefix.HasValue)
        {
            var args = new RedisValue[values.Length + 1];
            args[0] = prefix.GetValueOrDefault();
            Array.Copy(values, 0, args, 1, values.Length);
            return Message.Create(Database, flags, command, key, args);
        }

        return Message.Create(Database, flags, command, key, values);
    }

    private Message? GetArrayLastItemsMessage(RedisKey key, long count, bool reverse, CommandFlags flags)
    {
        CheckNonNegative(count, nameof(count));
        if (count == 0) return null;

        return reverse
            ? Message.Create(Database, flags, RedisCommand.ARLASTITEMS, key, count, RedisLiterals.REV)
            : Message.Create(Database, flags, RedisCommand.ARLASTITEMS, key, count);
    }
}
