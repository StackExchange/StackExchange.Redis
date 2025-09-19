using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    // Async String methods
    public Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StringBitCountAsync(RedisKey key, long start, long end, CommandFlags flags) =>
        throw new NotImplementedException();

    public Task<long> StringBitCountAsync(
        RedisKey key,
        long start = 0,
        long end = -1,
        StringIndexType indexType = StringIndexType.Byte,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StringBitOperationAsync(
        Bitwise operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second = default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StringBitOperationAsync(
        Bitwise operation,
        RedisKey destination,
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start, long end, CommandFlags flags) =>
        throw new NotImplementedException();

    public Task<long> StringBitPositionAsync(
        RedisKey key,
        bool bit,
        long start = 0,
        long end = -1,
        StringIndexType indexType = StringIndexType.Byte,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StringDecrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<double> StringDecrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> StringGetBitAsync(RedisKey key, long offset, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StringGetRangeAsync(
        RedisKey key,
        long start,
        long end,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StringGetSetExpiryAsync(
        RedisKey key,
        TimeSpan? expiry,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StringGetSetExpiryAsync(
        RedisKey key,
        DateTime expiry,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StringGetDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StringIncrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        => value == 1 ? StringIncrementUnitAsync(key, flags) : StringIncrementNonUnitAsync(key, value, flags);

    public Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<string?> StringLongestCommonSubsequenceAsync(
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> StringLongestCommonSubsequenceLengthAsync(
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<LCSMatchResult> StringLongestCommonSubsequenceWithMatchesAsync(
        RedisKey first,
        RedisKey second,
        long minLength = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when) =>
        StringSetAsync(key, value, expiry, false, when, CommandFlags.None);

    public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
        StringSetAsync(key, value, expiry, false, when, flags);

    public Task<bool> StringSetAsync(
        KeyValuePair<RedisKey, RedisValue>[] values,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StringSetAndGetAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan? expiry,
        When when,
        CommandFlags flags) =>
        throw new NotImplementedException();

    public Task<RedisValue> StringSetAndGetAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan? expiry = null,
        bool keepTtl = false,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> StringSetRangeAsync(
        RedisKey key,
        long offset,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous String methods
    public long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StringBitCount(RedisKey key, long start, long end, CommandFlags flags) =>
        throw new NotImplementedException();

    public long StringBitCount(
        RedisKey key,
        long start = 0,
        long end = -1,
        StringIndexType indexType = StringIndexType.Byte,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StringBitOperation(
        Bitwise operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second = default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StringBitOperation(
        Bitwise operation,
        RedisKey destination,
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StringBitPosition(RedisKey key, bool bit, long start, long end, CommandFlags flags) =>
        throw new NotImplementedException();

    public long StringBitPosition(
        RedisKey key,
        bool bit,
        long start = 0,
        long end = -1,
        StringIndexType indexType = StringIndexType.Byte,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StringDecrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public double StringDecrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    [RespCommand("get")]
    public partial RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None);

    public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    [RespCommand("get")]
    public partial Lease<byte>? StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None);

    public bool StringGetBit(RedisKey key, long offset, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StringGetSetExpiry(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StringGetSetExpiry(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StringGetDelete(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StringIncrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        => value == 1 ? StringIncrementUnit(key, flags) : StringIncrementNonUnit(key, value, flags);

    [RespCommand("incr")]
    private partial long StringIncrementUnit(RedisKey key, CommandFlags flags);

    [RespCommand("incrby")]
    private partial long StringIncrementNonUnit(RedisKey key, long value, CommandFlags flags);

    [RespCommand("incrbyfloat")]
    public partial double StringIncrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None);

    public long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public string? StringLongestCommonSubsequence(
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long StringLongestCommonSubsequenceLength(
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public LCSMatchResult StringLongestCommonSubsequenceWithMatches(
        RedisKey first,
        RedisKey second,
        long minLength = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when) =>
        StringSet(key, value, expiry, false, when, CommandFlags.None);

    public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
        StringSet(key, value, expiry, false, when, flags);

    public bool StringSet(
        RedisKey key,
        RedisValue value,
        TimeSpan? expiry = null,
        bool keepTtl = false,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => value.IsNull
            ? KeyDelete(key, flags)
            : StringSetCore(key, value, expiry.NullIfMaxValue(), keepTtl, when, flags);

    public Task<bool> StringSetAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan? expiry = null,
        bool keepTtl = false,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => value.IsNull
            ? KeyDeleteAsync(key, flags)
            : StringSetCoreAsync(key, value, expiry.NullIfMaxValue(), keepTtl, when, flags);

    [RespCommand("set", Formatter = StringSetFormatter.Formatter)]
    private partial bool StringSetCore(
        RedisKey key,
        RedisValue value,
        TimeSpan? expiry = null,
        bool keepTtl = false,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None);

    private sealed class StringSetFormatter : IRespFormatter<(RedisKey Key, RedisValue Value, TimeSpan? Expiry, bool
        KeepTtl,
        When When)>
    {
        public const string Formatter = $"{nameof(StringSetFormatter)}.{nameof(Instance)}";
        public static readonly StringSetFormatter Instance = new StringSetFormatter();
        private StringSetFormatter() { }

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, RedisValue Value, TimeSpan? Expiry, bool KeepTtl, When When) request)
        {
            // SET key value [NX | XX] [GET] [EX seconds | PX milliseconds |
            // EXAT unix-time-seconds | PXAT unix-time-milliseconds | KEEPTTL]
            var argCount = 2 + request.When switch
            {
                When.Always => 0,
                When.Exists or When.NotExists => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(request.When)),
            } + (request.Expiry.HasValue ? 2 : 0) + (request.KeepTtl ? 1 : 0);
            writer.WriteCommand(command, argCount);
            writer.Write(request.Key);
            writer.Write(request.Value);
            switch (request.When)
            {
                case When.Exists:
                    writer.WriteBulkString("EX"u8);
                    break;
                case When.NotExists:
                    writer.WriteBulkString("NX"u8);
                    break;
            }

            if (request.Expiry.HasValue)
            {
                var millis = (long)request.Expiry.Value.TotalMilliseconds;
                if ((millis % 1000) == 0)
                {
                    writer.WriteBulkString("EX"u8);
                    writer.WriteBulkString(millis / 1000);
                }
                else
                {
                    writer.WriteBulkString("PX"u8);
                    writer.WriteBulkString(millis);
                }
            }

            if (request.KeepTtl)
            {
                writer.WriteBulkString("KEEPTTL"u8);
            }
        }
    }

    public bool StringSet(
        KeyValuePair<RedisKey, RedisValue>[] values,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StringSetAndGet(
        RedisKey key,
        RedisValue value,
        TimeSpan? expiry,
        When when,
        CommandFlags flags) =>
        throw new NotImplementedException();

    public RedisValue StringSetAndGet(
        RedisKey key,
        RedisValue value,
        TimeSpan? expiry = null,
        bool keepTtl = false,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool StringSetBit(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue StringSetRange(
        RedisKey key,
        long offset,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
