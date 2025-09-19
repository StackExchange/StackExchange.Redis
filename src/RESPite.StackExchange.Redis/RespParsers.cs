using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

public static class RespParsers
{
    public static IRespParser<RedisValue> RedisValue => DefaultParser.Instance;
    public static IRespParser<RedisValue[]> RedisValueArray => DefaultParser.Instance;
    public static IRespParser<RedisKey> RedisKey => DefaultParser.Instance;
    public static IRespParser<Lease<byte>> BytesLease => DefaultParser.Instance;

    public static RedisValue ReadRedisValue(ref RespReader reader)
    {
        reader.DemandScalar();
        if (reader.IsNull) return global::StackExchange.Redis.RedisValue.Null;
        if (reader.TryReadInt64(out var i64)) return i64;
        if (reader.TryReadDouble(out var f64)) return f64;

        if (reader.UnsafeTryReadShortAscii(out var s)) return s;
        return reader.ReadByteArray();
    }

    public static RedisKey ReadRedisKey(ref RespReader reader)
    {
        reader.DemandScalar();
        if (reader.IsNull) return global::StackExchange.Redis.RedisKey.Null;
        if (reader.UnsafeTryReadShortAscii(out var s)) return s;
        return reader.ReadByteArray();
    }

    private sealed class DefaultParser : IRespParser<RedisValue>, IRespParser<RedisKey>,
        IRespParser<Lease<byte>>, IRespParser<RedisValue[]>
    {
        private DefaultParser() { }
        public static readonly DefaultParser Instance = new();

        RedisValue IRespParser<RedisValue>.Parse(ref RespReader reader)
            => ReadRedisValue(ref reader);

        RedisKey IRespParser<RedisKey>.Parse(ref RespReader reader)
            => ReadRedisKey(ref reader);

        Lease<byte> IRespParser<Lease<byte>>.Parse(ref RespReader reader)
        {
            reader.DemandScalar();
            if (reader.IsNull) return null!;
            var len = reader.ScalarLength();
            var lease = Lease<byte>.Create(len);
            reader.CopyTo(lease.Span);
            return lease;
        }

        public RedisValue[] Parse(ref RespReader reader) => reader.ReadArray(ReadRedisValue)!;
    }
}
