using System.Runtime.CompilerServices;
using RESPite.Internal;
using RESPite.Messages;
using StackExchange.Redis;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming
namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly HashCommands Hashes(this in RespContext context)
        => ref Unsafe.As<RespContext, HashCommands>(ref Unsafe.AsRef(in context));
}

public readonly struct HashCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class HashCommandsExtensions
{
    [RespCommand]
    public static partial RespOperation<bool> HDel(this in HashCommands context, RedisKey key, RedisValue field);

    [RespCommand]
    public static partial RespOperation<long> HDel(this in HashCommands context, RedisKey key, RedisValue[] fields);

    [RespCommand]
    public static partial RespOperation<bool> HExists(this in HashCommands context, RedisKey key, RedisValue field);

    [RespCommand(Parser = "ExpireResultParser.Default")]
    private static partial RespOperation<ExpireResult[]> HExpire(
        this in HashCommands context,
        RedisKey key,
        long seconds,
        [RespIgnore(ExpireWhen.Always)] ExpireWhen when,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand(Parser = "ExpireResultParser.Default")]
    private static partial RespOperation<ExpireResult[]> HExpireAt(
        this in HashCommands context,
        RedisKey key,
        long seconds,
        [RespIgnore(ExpireWhen.Always)] ExpireWhen when,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand(Parser = "ExpireResultParser.Default")]
    private static partial RespOperation<ExpireResult[]> HPExpire(
        this in HashCommands context,
        RedisKey key,
        long milliseconds,
        [RespIgnore(ExpireWhen.Always)] ExpireWhen when,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand(Parser = "ExpireResultParser.Default")]
    private static partial RespOperation<ExpireResult[]> HPExpireAt(
        this in HashCommands context,
        RedisKey key,
        long milliseconds,
        [RespIgnore(ExpireWhen.Always)] ExpireWhen when,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    private sealed class ExpireResultParser : IRespParser<ExpireResult>, IRespParser<ExpireResult[]>
    {
        private ExpireResultParser() { }
        public static readonly ExpireResultParser Default = new();

        ExpireResult IRespParser<ExpireResult>.Parse(ref RespReader reader)
        {
            if (reader.IsAggregate & !reader.IsNull)
            {
                // if aggregate: take the first element
                reader.MoveNext();
            }

            // otherwise, take first from array
            return (ExpireResult)reader.ReadInt64();
        }

        ExpireResult[] IRespParser<ExpireResult[]>.Parse(ref RespReader reader)
            => reader.ReadArray(static (ref RespReader reader) => (ExpireResult)reader.ReadInt64(), scalar: true)!;
    }

    internal static RespOperation<ExpireResult[]> HExpire(
        this in HashCommands context,
        RedisKey key,
        TimeSpan expiry,
        ExpireWhen when,
        RedisValue[] fields)
    {
        var millis = (long)expiry.TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return HExpire(context, key, millis / 1000, when, fields);
        }

        return HPExpire(context, key, millis, when, fields);
    }

    internal static RespOperation<ExpireResult[]> HExpireAt(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        ExpireWhen when,
        RedisValue[] fields)
    {
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HExpireAt(context, key, millis / 1000, when, fields);
        }

        return HPExpireAt(context, key, millis, when, fields);
    }

    [RespCommand(Parser = "RespParsers.DateTimeFromSeconds")]
    public static partial RespOperation<DateTime?> HExpireTime(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(Parser = "RespParsers.DateTimeArrayFromSeconds")]
    public static partial RespOperation<DateTime?[]> HExpireTime(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand(nameof(HPExpireTime))]
    public static partial RespOperation<long> HPExpireTimeRaw(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(nameof(HPExpireTime))]
    public static partial RespOperation<long[]> HPExpireTimeRaw(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand(Parser = "RespParsers.DateTimeFromMilliseconds")]
    public static partial RespOperation<DateTime?> HPExpireTime(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(Parser = "RespParsers.DateTimeArrayFromMilliseconds")]
    public static partial RespOperation<DateTime?[]> HPExpireTime(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand]
    public static partial RespOperation<RedisValue> HGet(
        this in HashCommands context,
        RedisKey key,
        RedisValue field);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> HGetDel(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand]
    public static partial RespOperation<RedisValue> HGetDel(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")]
        RedisValue fields);

    [RespCommand(nameof(HGetDel))]
    public static partial RespOperation<Lease<byte>?> HGetDelLease(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")]
        RedisValue fields);

    public static RespOperation<RedisValue> HGetEx(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        bool persist = false)
        => HGetEx(context, key, persist ? HGetExMode.PERSIST : HGetExMode.None, -1, field);

    public static RespOperation<Lease<byte>?> HGetExLease(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        bool persist = false)
        => HGetExLease(context, key, persist ? HGetExMode.PERSIST : HGetExMode.None, -1, field);

    internal static RespOperation<Lease<byte>?> HGetExLease(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        TimeSpan? expiry,
        bool persist)
        => expiry.HasValue
            ? HGetExLease(context, key, expiry.GetValueOrDefault(), field)
            : HGetExLease(context, key, field, persist);

    internal static RespOperation<RedisValue> HGetEx(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        TimeSpan? expiry,
        bool persist)
        => expiry.HasValue
            ? HGetEx(context, key, expiry.GetValueOrDefault(), field)
            : HGetEx(context, key, field, persist);

    internal static RespOperation<RedisValue[]> HGetEx(
        this in HashCommands context,
        RedisKey key,
        RedisValue[] fields,
        TimeSpan? expiry,
        bool persist)
        => expiry.HasValue
            ? HGetEx(context, key, expiry.GetValueOrDefault(), fields)
            : HGetEx(context, key, fields, persist);

    public static RespOperation<RedisValue[]> HGetEx(
        this in HashCommands context,
        RedisKey key,
        RedisValue[] fields,
        bool persist = false)
        => HGetEx(context, key, persist ? HGetExMode.PERSIST : HGetExMode.None, -1, fields);

    public static RespOperation<RedisValue> HGetEx(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        RedisValue field)
    {
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HGetEx(context, key, HGetExMode.EXAT, millis / 1000, field);
        }

        return HGetEx(context, key, HGetExMode.PXAT, millis, field);
    }

    public static RespOperation<Lease<byte>?> HGetExLease(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        RedisValue field)
    {
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HGetExLease(context, key, HGetExMode.EXAT, millis / 1000, field);
        }

        return HGetExLease(context, key, HGetExMode.PXAT, millis, field);
    }

    public static RespOperation<RedisValue[]> HGetEx(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        RedisValue[] fields)
    {
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HGetEx(context, key, HGetExMode.EXAT, millis / 1000, fields);
        }

        return HGetEx(context, key, HGetExMode.PXAT, millis, fields);
    }

    public static RespOperation<RedisValue> HGetEx(
        this in HashCommands context,
        RedisKey key,
        TimeSpan expiry,
        RedisValue field)
    {
        var millis = (long)expiry.TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return HGetEx(context, key, HGetExMode.EX, millis / 1000, field);
        }

        return HGetEx(context, key, HGetExMode.PX, millis, field);
    }

    public static RespOperation<Lease<byte>?> HGetExLease(
        this in HashCommands context,
        RedisKey key,
        TimeSpan expiry,
        RedisValue field)
    {
        var millis = (long)expiry.TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return HGetExLease(context, key, HGetExMode.EX, millis / 1000, field);
        }

        return HGetExLease(context, key, HGetExMode.PX, millis, field);
    }

    public static RespOperation<RedisValue[]> HGetEx(
        this in HashCommands context,
        RedisKey key,
        TimeSpan expiry,
        RedisValue[] fields)
    {
        var millis = (long)expiry.TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return HGetEx(context, key, HGetExMode.EXAT, millis / 1000, fields);
        }

        return HGetEx(context, key, HGetExMode.PXAT, millis, fields);
    }

    internal enum HGetExMode
    {
        None,
        EX,
        PX,
        EXAT,
        PXAT,
        PERSIST,
    }

    [RespCommand]
    private static partial RespOperation<RedisValue[]> HGetEx(
        this in HashCommands context,
        RedisKey key,
        [RespIgnore(HGetExMode.None)] HGetExMode mode,
        [RespIgnore(-1)] long value,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand]
    private static partial RespOperation<RedisValue> HGetEx(
        this in HashCommands context,
        RedisKey key,
        [RespIgnore(HGetExMode.None)] HGetExMode mode,
        [RespIgnore(-1)] long value,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(nameof(HGetEx))]
    private static partial RespOperation<Lease<byte>?> HGetExLease(
        this in HashCommands context,
        RedisKey key,
        [RespIgnore(HGetExMode.None)] HGetExMode mode,
        [RespIgnore(-1)] long value,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand("hget")]
    public static partial RespOperation<Lease<byte>?> HGetLease(
        this in HashCommands context,
        RedisKey key,
        RedisValue field);

    [RespCommand]
    public static partial RespOperation<HashEntry[]> HGetAll(this in HashCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> HIncrBy(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        long value = 1);

    [RespCommand]
    public static partial RespOperation<double> HIncrByFloat(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        double value);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> HKeys(this in HashCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> HLen(this in HashCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> HMGet(
        this in HashCommands context,
        RedisKey key,
        RedisValue[] fields);

    [RespCommand]
    public static partial RespOperation<RedisValue> HRandField(this in HashCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]>
        HRandField(this in HashCommands context, RedisKey key, long count);

    [RespCommand]
    public static partial RespOperation<HashEntry[]> HRandFieldWithValues(
        this in HashCommands context,
        RedisKey key,
        [RespSuffix("WITHVALUES")] long count);

    [RespCommand]
    public static partial RespOperation<bool> HSet(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        RedisValue value);

    internal static RespOperation<bool> HSet(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        RedisValue value,
        When when)
    {
        switch (when)
        {
            case When.Always:
                return HSet(context, key, field, value);
            case When.NotExists:
                return HSetNX(context, key, field, value);
            default:
                when.AlwaysOrNotExists(); // throws
                return default;
        }
    }

    [RespCommand(Formatter = "HSetFormatter.Instance")]
    public static partial RespOperation HSet(this in HashCommands context, RedisKey key, HashEntry[] fields);

    private sealed class HSetFormatter : IRespFormatter<(RedisKey Key, HashEntry[] Fields)>
    {
        private HSetFormatter() { }
        public static readonly HSetFormatter Instance = new();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, HashEntry[] Fields) request)
        {
            writer.WriteCommand(command, 1 + (request.Fields.Length * 2));
            writer.Write(request.Key);
            foreach (var entry in request.Fields)
            {
                writer.Write(entry.Name);
                writer.Write(entry.Value);
            }
        }
    }

    [RespCommand]
    public static partial RespOperation<bool> HSetNX(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        RedisValue value);

    [RespCommand]
    public static partial RespOperation<long> HStrLen(this in HashCommands context, RedisKey key, RedisValue field);

    [RespCommand(Parser = "PersistResultParser.Default")]
    public static partial RespOperation<PersistResult> HPersist(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(Parser = "PersistResultParser.Default")]
    public static partial RespOperation<PersistResult[]> HPersist(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    private sealed class PersistResultParser : IRespParser<PersistResult>, IRespParser<PersistResult[]>, IRespInlineParser
    {
        private PersistResultParser() { }
        public static readonly PersistResultParser Default = new();
        PersistResult IRespParser<PersistResult>.Parse(ref RespReader reader)
        {
            if (reader.IsAggregate)
            {
                reader.MoveNext(); // read first element from array
            }
            return (PersistResult)reader.ReadInt64();
        }

        PersistResult[] IRespParser<PersistResult[]>.Parse(ref RespReader reader) => reader.ReadArray(
            static (ref RespReader reader) => (PersistResult)reader.ReadInt64(),
            scalar: true)!;
    }

    [RespCommand(nameof(HPTtl))]
    public static partial RespOperation<long> HPTtlRaw(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")]
        RedisValue field);

    [RespCommand(nameof(HPTtl))]
    public static partial RespOperation<long[]> HPTtlRaw(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand(Parser = "RespParsers.TimeSpanFromMilliseconds")]
    public static partial RespOperation<TimeSpan?> HPTtl(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(Parser = "RespParsers.TimeSpanArrayFromMilliseconds")]
    public static partial RespOperation<TimeSpan?[]> HPTtl(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand(nameof(HTtl))]
    public static partial RespOperation<long> HTtlRaw(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")]
        RedisValue field);

    [RespCommand(nameof(HTtl))]
    public static partial RespOperation<long[]> HTtlRaw(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand(Parser = "RespParsers.TimeSpanFromSeconds")]
    public static partial RespOperation<TimeSpan?> HTtl(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(Parser = "RespParsers.TimeSpanArrayFromSeconds")]
    public static partial RespOperation<TimeSpan?[]> HTtl(
        this in HashCommands context,
        RedisKey key,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> HVals(this in HashCommands context, RedisKey key);
}
