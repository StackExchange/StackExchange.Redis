using System;
using System.Threading.Tasks;
using RESPite.Messages;

namespace StackExchange.Redis;

internal static class RespReaderExtensions
{
    extension(in RespReader reader)
    {
        public RedisValue ReadRedisValue()
        {
            reader.DemandScalar();
            if (reader.IsNull) return RedisValue.Null;

            return reader.Prefix switch
            {
                RespPrefix.Boolean => reader.ReadBoolean(),
                RespPrefix.Integer => reader.ReadInt64(),
                _ => reader.ReadByteArray(),
            };
        }

        public RedisKey ReadRedisKey()
        {
            reader.DemandScalar();
            return (RedisKey)reader.ReadByteArray();
        }

        public string GetOverview()
        {
            if (reader.IsNull) return "(null)";

            return reader.Prefix switch
            {
                RespPrefix.SimpleString or RespPrefix.Integer or RespPrefix.SimpleError or RespPrefix.Double => $"{reader.Prefix}: {reader.ReadString()}",
                _ when reader.IsScalar => $"{reader.Prefix}: {reader.ScalarLength()} bytes",
                _ when reader.IsAggregate => $"{reader.Prefix}: {reader.AggregateLength()} items",
                _ => $"(unknown: {reader.Prefix})",
            };
        }

        public RespPrefix GetFirstPrefix()
        {
            var prefix = reader.Prefix;
            if (prefix is RespPrefix.None)
            {
                var mutable = reader;
                mutable.MovePastBof();
                prefix = mutable.Prefix;
            }
            return prefix;
        }

        /*
        public bool AggregateHasAtLeast(int count)
        {
            reader.DemandAggregate();
            if (reader.IsNull) return false;
            if (reader.IsStreaming) return CheckStreamingAggregateAtLeast(in reader, count);
            return reader.AggregateLength() >= count;

            static bool CheckStreamingAggregateAtLeast(in RespReader reader, int count)
            {
                var iter = reader.AggregateChildren();
                object? attributes = null;
                while (count > 0 && iter.MoveNextRaw(null!, ref attributes))
                {
                    count--;
                }

                return count == 0;
            }
        }
        */
    }

    extension(ref RespReader reader)
    {
        public bool SafeTryMoveNext() => reader.TryMoveNext(checkError: false) & !reader.IsError;

        public void MovePastBof()
        {
            // if we're at BOF, read the first element, ignoring errors
            if (reader.Prefix is RespPrefix.None) reader.SafeTryMoveNext();
        }

        public RedisValue[]? ReadPastRedisValues()
            => reader.ReadPastArray(static (ref r) => r.ReadRedisValue(), scalar: true);
    }

    public static RespPrefix GetRespPrefix(ReadOnlySpan<byte> frame)
    {
        var reader = new RespReader(frame);
        reader.SafeTryMoveNext();
        return reader.Prefix;
    }

    extension(RespPrefix prefix)
    {
        public ResultType ToResultType() => prefix switch
        {
            RespPrefix.Array => ResultType.Array,
            RespPrefix.Attribute => ResultType.Attribute,
            RespPrefix.BigInteger => ResultType.BigInteger,
            RespPrefix.Boolean => ResultType.Boolean,
            RespPrefix.BulkError => ResultType.BlobError,
            RespPrefix.BulkString => ResultType.BulkString,
            RespPrefix.SimpleString => ResultType.SimpleString,
            RespPrefix.Map => ResultType.Map,
            RespPrefix.Set => ResultType.Set,
            RespPrefix.Double => ResultType.Double,
            RespPrefix.Integer => ResultType.Integer,
            RespPrefix.SimpleError => ResultType.Error,
            RespPrefix.Null => ResultType.Null,
            RespPrefix.VerbatimString => ResultType.VerbatimString,
            RespPrefix.Push=> ResultType.Push,
            _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, null),
        };
    }

    extension<T>(T?[] array) where T : class
    {
        internal bool AnyNull()
        {
            foreach (var el in array)
            {
                if (el is null) return true;
            }

            return false;
        }
    }

#if !(NET || NETSTANDARD2_1_OR_GREATER)
    extension(Task task)
    {
        public bool IsCompletedSuccessfully => task.Status is TaskStatus.RanToCompletion;
    }
#endif
}
