using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

public static class RespParsers
{
    public static IRespParser<RedisValue> RedisValue => DefaultParser.Instance;
    public static IRespParser<RedisKey> RedisKey => DefaultParser.Instance;

    private sealed class DefaultParser : IRespParser<RedisValue>, IRespParser<RedisKey>
    {
        private DefaultParser() { }
        public static readonly DefaultParser Instance = new();

        RedisValue IRespParser<RedisValue>.Parse(ref RespReader reader)
        {
            reader.DemandScalar();
            if (reader.IsNull) return global::StackExchange.Redis.RedisValue.Null;
            if (reader.TryReadInt64(out var i64)) return i64;
            if (reader.TryReadDouble(out var f64)) return f64;

            if (reader.UnsafeTryReadShortAscii(out var s)) return s;
            return reader.ReadByteArray();
        }

        RedisKey IRespParser<RedisKey>.Parse(ref RespReader reader)
        {
            reader.DemandScalar();
            if (reader.IsNull) return global::StackExchange.Redis.RedisKey.Null;
            if (reader.UnsafeTryReadShortAscii(out var s)) return s;
            return reader.ReadByteArray();
        }
    }
}
