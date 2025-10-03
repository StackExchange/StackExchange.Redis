using RESPite.Internal;
using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

public static class RespParsers
{
    public static IRespParser<RedisValue> RedisValue => DefaultParser.Instance;
    public static IRespParser<RedisValue[]> RedisValueArray => DefaultParser.Instance;
    public static IRespParser<RedisKey> RedisKey => DefaultParser.Instance;
    public static IRespParser<Lease<byte>> BytesLease => DefaultParser.Instance;
    public static IRespParser<HashEntry[]> HashEntryArray => DefaultParser.Instance;
    public static IRespParser<TimeSpan?> TimeSpanFromSeconds => TimeParser.FromSeconds;
    public static IRespParser<TimeSpan?[]> TimeSpanArrayFromSeconds => TimeParser.FromSeconds;
    public static IRespParser<DateTime?> DateTimeFromSeconds => TimeParser.FromSeconds;
    public static IRespParser<DateTime?[]> DateTimeArrayFromSeconds => TimeParser.FromSeconds;
    public static IRespParser<TimeSpan?> TimeSpanFromMilliseconds => TimeParser.FromMilliseconds;
    public static IRespParser<TimeSpan?[]> TimeSpanArrayFromMilliseconds => TimeParser.FromMilliseconds;
    public static IRespParser<DateTime?> DateTimeFromMilliseconds => TimeParser.FromMilliseconds;
    public static IRespParser<DateTime?[]> DateTimeArrayFromMilliseconds => TimeParser.FromMilliseconds;
    internal static IRespParser<long> Int64Index => Int64DefaultNegativeOneParser.Instance;
    internal static IRespParser<ListPopResult> ListPopResult => DefaultParser.Instance;

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

    private static readonly RespReader.Projection<RedisValue> SharedReadRedisValue = ReadRedisValue;
    private static readonly RespReader.Projection<RedisKey> SharedReadRedisKey = ReadRedisKey;

    private sealed class DefaultParser : IRespParser<RedisValue>, IRespParser<RedisKey>,
        IRespParser<Lease<byte>>, IRespParser<RedisValue[]>, IRespParser<HashEntry[]>,
        IRespParser<RedisKey[]>, IRespParser<ListPopResult>
    {
        private DefaultParser() { }
        public static readonly DefaultParser Instance = new();

        RedisValue IRespParser<RedisValue>.Parse(ref RespReader reader) => ReadRedisValue(ref reader);

        RedisKey IRespParser<RedisKey>.Parse(ref RespReader reader) => ReadRedisKey(ref reader);

        Lease<byte> IRespParser<Lease<byte>>.Parse(ref RespReader reader)
        {
            reader.DemandScalar();
            if (reader.IsNull) return null!;
            var len = reader.ScalarLength();
            var lease = Lease<byte>.Create(len);
            reader.CopyTo(lease.Span);
            return lease;
        }

        RedisValue[] IRespParser<RedisValue[]>.Parse(ref RespReader reader)
            => reader.ReadArray(SharedReadRedisValue, scalar: true)!;

        RedisKey[] IRespParser<RedisKey[]>.Parse(ref RespReader reader)
            => reader.ReadArray(SharedReadRedisKey, scalar: true)!;

        HashEntry[] IRespParser<HashEntry[]>.Parse(ref RespReader reader)
        {
            return reader.ReadPairArray(
                SharedReadRedisValue,
                SharedReadRedisValue,
                static (x, y) => new HashEntry(x, y),
                scalar: true)!;

            /* we could also do this locally:
            reader.DemandAggregate();
            if (reader.IsNull) return null!;
            var len = reader.AggregateLength() / 2;
            if (len == 0) return [];

            var result = new HashEntry[len];
            for (int i = 0; i < result.Length; i++)
            {
                reader.MoveNextScalar();
                var x = ReadRedisValue(ref reader);
                reader.MoveNextScalar();
                var y = ReadRedisValue(ref reader);
                result[i] = new HashEntry(x, y);
            }

            return result;
            */
        }

        ListPopResult IRespParser<ListPopResult>.Parse(ref RespReader reader)
        {
            if (reader.IsNull) return global::StackExchange.Redis.ListPopResult.Null;
            reader.DemandAggregate();
            reader.MoveNext();
            var key = ReadRedisKey(ref reader);
            reader.MoveNext();
            var arr = reader.ReadArray(SharedReadRedisValue, scalar: true)!;
            return new(key, arr);
        }
    }
}

internal sealed class Int64DefaultNegativeOneParser : IRespParser<long>, IRespInlineParser
{
    private Int64DefaultNegativeOneParser() { }
    public static readonly Int64DefaultNegativeOneParser Instance = new();
    public long Parse(ref RespReader reader) => reader.IsNull ? -1 : reader.ReadInt64();
}

internal sealed class TimeParser : IRespParser<TimeSpan?>, IRespParser<DateTime?>, IRespInlineParser,
    IRespParser<TimeSpan?[]>, IRespParser<DateTime?[]>
{
    private readonly bool _millis;
    public static readonly TimeParser FromMilliseconds = new(true);
    public static readonly TimeParser FromSeconds = new(false);

    private readonly RespReader.Projection<TimeSpan?> _readTimeSpan;
    private readonly RespReader.Projection<DateTime?> _readDateTime;
    private TimeParser(bool millis)
    {
        _millis = millis;
        _readTimeSpan = ReadTimeSpan;
        _readDateTime = ReadDateTime;
    }

    TimeSpan? IRespParser<TimeSpan?>.Parse(ref RespReader reader) => ReadTimeSpan(ref reader);
    private TimeSpan? ReadTimeSpan(ref RespReader reader)
    {
        if (reader.IsNull) return null;
        if (reader.IsAggregate)
        {
            reader.MoveNext(); // take first element from aggregate
            if (reader.IsNull) return null;
        }
        var value = reader.ReadInt64();
        if (value < 0) return null; // -1 means no expiry and -2 means key does not exist
        return _millis ? TimeSpan.FromMilliseconds(value) : TimeSpan.FromSeconds(value);
    }

    DateTime? IRespParser<DateTime?>.Parse(ref RespReader reader) => ReadDateTime(ref reader);
    private DateTime? ReadDateTime(ref RespReader reader)
    {
        if (reader.IsNull) return null;
        if (reader.IsAggregate)
        {
            reader.MoveNext(); // take first element from aggregate
            if (reader.IsNull) return null;
        }
        var value = reader.ReadInt64();
        if (value < 0) return null; // -1 means no expiry and -2 means key does not exist
        return _millis ? RedisBase.UnixEpoch.AddMilliseconds(value) : RedisBase.UnixEpoch.AddSeconds(value);
    }

    TimeSpan?[] IRespParser<TimeSpan?[]>.Parse(ref RespReader reader) => reader.ReadArray(_readTimeSpan, scalar: true)!;

    DateTime?[] IRespParser<DateTime?[]>.Parse(ref RespReader reader) => reader.ReadArray(_readDateTime, scalar: true)!;
}
