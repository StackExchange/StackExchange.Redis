using System;
using RESPite.Messages;

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

    private static bool TryParseArrayIndex(ref RespReader reader, out RedisArrayIndex index)
    {
        if (reader.IsScalar && !reader.IsNull)
        {
            unsafe
            {
                if (reader.TryParseScalar(&Format.TryParseUInt64, out ulong value))
                {
                    index = new RedisArrayIndex(value);
                    return true;
                }
            }
        }

        index = default;
        return false;
    }

    private sealed class RedisArrayIndexProcessor : ResultProcessor<RedisArrayIndex>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (TryParseArrayIndex(ref reader, out RedisArrayIndex index))
            {
                SetResult(message, index);
                return true;
            }

            return false;
        }
    }

    private sealed class NullableRedisArrayIndexProcessor : ResultProcessor<RedisArrayIndex?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsScalar && reader.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            if (TryParseArrayIndex(ref reader, out RedisArrayIndex index))
            {
                SetResult(message, index);
                return true;
            }

            return false;
        }
    }

    private sealed class RedisArrayEntryArrayProcessor : ValuePairInterleavedProcessorBase<RedisArrayEntry>
    {
        protected override bool AllowJaggedPairs(RedisProtocol protocol) => true; // i.e. even in RESP2

        protected override RedisArrayEntry Parse(ref RespReader first, ref RespReader second, object? state)
        {
            TryParseArrayIndex(ref first, out RedisArrayIndex index);
            return new RedisArrayEntry(index, second.ReadRedisValue());
        }
    }

    private sealed class RedisArrayIndexEntryArrayProcessor : ArrayResultProcessor<RedisArrayEntry>
    {
        protected override bool TryParse(ref RespReader reader, out RedisArrayEntry parsed)
        {
            if (TryParseArrayIndex(ref reader, out RedisArrayIndex index))
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
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (!reader.IsAggregate || reader.IsNull)
            {
                return false;
            }

            RedisArrayIndex count = default, length = default, nextInsertIndex = default, slices = default, directorySize = default, superDirEntries = default, sliceSize = default;
            var iter = reader.AggregateChildren();
            while (iter.MoveNext())
            {
                unsafe
                {
                    if (!iter.Value.TryParseScalar(&ArrayInfoFieldMetadata.TryParse, out ArrayInfoField field))
                    {
                        field = ArrayInfoField.Unknown;
                    }

                    if (!iter.MoveNext())
                    {
                        break;
                    }

                    var value = iter.Value;
                    if (!TryParseArrayIndex(ref value, out RedisArrayIndex index))
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
            }

            SetResult(message, new ArrayInfo(count, length, nextInsertIndex, slices, directorySize, superDirEntries, sliceSize));
            return true;
        }
    }
}
