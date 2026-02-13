using System;
using RESPite.Messages;

namespace StackExchange.Redis;

internal static class RespReaderExtensions
{
    extension(in RespReader reader)
    {
        public RespPrefix Resp2PrefixBulkString => reader.Prefix.ToResp2(RespPrefix.BulkString);
        // if null, assume array
        public RespPrefix Resp2PrefixArray => reader.Prefix.ToResp2(RespPrefix.Array);

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

        public string OverviewString()
        {
            if (reader.IsNull) return "(null)";

            return reader.Resp2PrefixBulkString switch
            {
                RespPrefix.SimpleString or RespPrefix.Integer or RespPrefix.SimpleError => $"{reader.Prefix}: {reader.ReadString()}",
                _ when reader.IsScalar => $"{reader.Prefix}: {reader.ScalarLength()} bytes",
                _ when reader.IsAggregate => $"{reader.Prefix}: {reader.AggregateLength()} items",
                _ => $"(unknown: {reader.Prefix})",
            };
        }
    }

    extension(ref RespReader reader)
    {
        public bool SafeTryMoveNext() => reader.TryMoveNext(checkError: false) & !reader.IsError;
    }

    public static RespPrefix GetRespPrefix(ReadOnlySpan<byte> frame)
    {
        var reader = new RespReader(frame);
        reader.SafeTryMoveNext();
        return reader.Prefix;
    }

    extension(RespPrefix prefix)
    {
        public RespPrefix ToResp2(RespPrefix nullValue)
        {
            return prefix switch
            {
                // null: map to what the caller prefers
                RespPrefix.Null => nullValue,
                // RESP 3: map to closest RESP 2 equivalent
                RespPrefix.Boolean => RespPrefix.Integer,
                RespPrefix.Double or RespPrefix.BigInteger => RespPrefix.SimpleString,
                RespPrefix.BulkError => RespPrefix.SimpleError,
                RespPrefix.VerbatimString => RespPrefix.BulkString,
                RespPrefix.Map or RespPrefix.Set or RespPrefix.Push or RespPrefix.Attribute => RespPrefix.Array,
                // RESP 2 or anything exotic: leave alone
                _ => prefix,
            };
        }
    }
}
