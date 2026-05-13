using System;

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
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.Resp2TypeArray != ResultType.Array)
            {
                return false;
            }

            if (result.IsNull)
            {
                SetResult(message, Array.Empty<RedisArrayEntry>());
                return true;
            }

            return base.SetResultCore(connection, message, result);
        }

        protected override RedisArrayEntry Parse(in RawResult first, in RawResult second, object? state)
        {
            TryParseArrayIndex(first, out RedisArrayIndex index);
            return new RedisArrayEntry(index, second.AsRedisValue());
        }
    }

    private sealed class RedisArrayIndexEntryArrayProcessor : ResultProcessor<RedisArrayEntry[]>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.Resp2TypeArray != ResultType.Array)
            {
                return false;
            }

            if (result.IsNull)
            {
                SetResult(message, Array.Empty<RedisArrayEntry>());
                return true;
            }

            var items = result.GetItems();
            var count = checked((int)items.Length);
            var entries = new RedisArrayEntry[count];
            var iter = items.GetEnumerator();
            for (int i = 0; i < entries.Length; i++)
            {
                if (!iter.MoveNext() || !TryParseArrayIndex(iter.Current, out RedisArrayIndex index))
                {
                    return false;
                }

                entries[i] = new RedisArrayEntry(index);
            }

            SetResult(message, entries);
            return true;
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

            RedisArrayIndex count = default, length = default, nextInsertIndex = default, slices = default, directorySize = default, superDirEntries = default, sliceSize = default;
            var iter = result.GetItems().GetEnumerator();
            while (iter.MoveNext())
            {
                if (!iter.Current.TryParse(ArrayInfoFieldMetadata.TryParse, out ArrayInfoField field))
                {
                    field = ArrayInfoField.Unknown;
                }

                if (!iter.MoveNext())
                {
                    break;
                }

                ref readonly RawResult value = ref iter.Current;
                if (!TryParseArrayIndex(value, out RedisArrayIndex index))
                {
                    continue;
                }

                switch (field)
                {
                    case ArrayInfoField.Count:
                        count = index;
                        break;
                    case ArrayInfoField.Length:
                        length = index;
                        break;
                    case ArrayInfoField.NextInsertIndex:
                        nextInsertIndex = index;
                        break;
                    case ArrayInfoField.Slices:
                        slices = index;
                        break;
                    case ArrayInfoField.DirectorySize:
                        directorySize = index;
                        break;
                    case ArrayInfoField.SuperDirEntries:
                        superDirEntries = index;
                        break;
                    case ArrayInfoField.SliceSize:
                        sliceSize = index;
                        break;
                }
            }

            SetResult(message, new ArrayInfo(count, length, nextInsertIndex, slices, directorySize, superDirEntries, sliceSize));
            return true;
        }
    }
}
