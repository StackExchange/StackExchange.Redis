using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    public static readonly ResultProcessor<RedisArrayIndex>
        RedisArrayIndex = new RedisArrayIndexProcessor();

    public static readonly ResultProcessor<RedisArrayIndex?>
        NullableRedisArrayIndex = new NullableRedisArrayIndexProcessor();

    public static readonly ResultProcessor<RedisArrayEntry[]>
        RedisArrayEntryArray = new RedisArrayEntryArrayProcessor();

    public static readonly ResultProcessor<RedisArrayEntry[]>
        RedisArrayIndexEntryArray = new RedisArrayIndexEntryArrayProcessor();

    public static readonly ResultProcessor<ArrayInfo>
        ArrayInfo = new ArrayInfoProcessor();

    private static bool TryParseArrayIndex(in RawResult result, out RedisArrayIndex index)
    {
        switch (result.Resp2TypeBulkString)
        {
            case ResultType.Integer:
            case ResultType.SimpleString:
            case ResultType.BulkString:
                if (!result.IsNull && result.TryParse(Format.TryParseUInt64, out ulong value))
                {
                    index = new RedisArrayIndex(value);
                    return true;
                }
                break;
        }

        index = default;
        return false;
    }

    private sealed class RedisArrayIndexProcessor : ResultProcessor<RedisArrayIndex>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (TryParseArrayIndex(result, out RedisArrayIndex index))
            {
                SetResult(message, index);
                return true;
            }

            return false;
        }
    }

    private sealed class NullableRedisArrayIndexProcessor : ResultProcessor<RedisArrayIndex?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.Resp2TypeBulkString == ResultType.BulkString && result.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            if (TryParseArrayIndex(result, out RedisArrayIndex index))
            {
                SetResult(message, index);
                return true;
            }

            return false;
        }
    }

    private sealed class RedisArrayEntryArrayProcessor : ValuePairInterleavedProcessorBase<RedisArrayEntry>
    {
        protected override bool AllowJaggedPairs(in RawResult result) => true; // i.e. even in RESP2

        protected override RedisArrayEntry Parse(in RawResult first, in RawResult second, object? state)
        {
            TryParseArrayIndex(first, out RedisArrayIndex index);
            return new RedisArrayEntry(index, second.AsRedisValue());
        }
    }

    private sealed class RedisArrayIndexEntryArrayProcessor : ArrayResultProcessor<RedisArrayEntry>
    {
        protected override bool TryParse(in RawResult raw, out RedisArrayEntry parsed)
        {
            if (TryParseArrayIndex(raw, out RedisArrayIndex index))
            {
                parsed = new RedisArrayEntry(index);
                return true;
            }

            parsed = default;
            return false;
        }
    }

    private sealed class ArrayInfoProcessor : ResultProcessor<ArrayInfo>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.Resp2TypeArray != ResultType.Array || result.IsNull)
            {
                return false;
            }

            var lease = ArrayPool<KeyValuePair<string, RedisValue>>.Shared.Rent(result.ItemsCount / 2);
            int count = 0;
            var iter = result.GetItems().GetEnumerator();
            while (iter.MoveNext())
            {
                // try to parse the field as a known enum, and get the known string for it, otherwise: alloc
                if (!(iter.Current.TryParse(ArrayInfoFieldMetadata.TryParse, out ArrayInfoField field)
                      && ArrayInfoFieldMetadata.TryFormat(field, out var key)))
                {
                    key = iter.Current.GetString() ?? "";
                }

                if (!iter.MoveNext())
                {
                    break;
                }

                try
                {
                    lease[count++] = new(key, iter.Current.AsRedisValue());
                }
                catch (Exception ex)
                {
                    // quietly ignore non-scalar results
                    Debug.WriteLine(ex.Message);
                }
            }

            SetResult(message, new ArrayInfo(new(lease, 0, count)));
            ArrayPool<KeyValuePair<string, RedisValue>>.Shared.Return(lease);
            return true;
        }
    }
}
