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
        => HGetEx(context, key, persist ? HashExpiryMode.PERSIST : HashExpiryMode.None, -1, field);

    public static RespOperation<Lease<byte>?> HGetExLease(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        bool persist = false)
        => HGetExLease(context, key, persist ? HashExpiryMode.PERSIST : HashExpiryMode.None, -1, field);

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
        => HGetEx(context, key, persist ? HashExpiryMode.PERSIST : HashExpiryMode.None, -1, fields);

    public static RespOperation<RedisValue> HGetEx(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        RedisValue field)
    {
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HGetEx(context, key, HashExpiryMode.EXAT, millis / 1000, field);
        }

        return HGetEx(context, key, HashExpiryMode.PXAT, millis, field);
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
            return HGetExLease(context, key, HashExpiryMode.EXAT, millis / 1000, field);
        }

        return HGetExLease(context, key, HashExpiryMode.PXAT, millis, field);
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
            return HGetEx(context, key, HashExpiryMode.EXAT, millis / 1000, fields);
        }

        return HGetEx(context, key, HashExpiryMode.PXAT, millis, fields);
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
            return HGetEx(context, key, HashExpiryMode.EX, millis / 1000, field);
        }

        return HGetEx(context, key, HashExpiryMode.PX, millis, field);
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
            return HGetExLease(context, key, HashExpiryMode.EX, millis / 1000, field);
        }

        return HGetExLease(context, key, HashExpiryMode.PX, millis, field);
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
            return HGetEx(context, key, HashExpiryMode.EXAT, millis / 1000, fields);
        }

        return HGetEx(context, key, HashExpiryMode.PXAT, millis, fields);
    }

    internal enum HashExpiryMode
    {
        None,
        EX,
        PX,
        EXAT,
        PXAT,
        PERSIST,
        KEEPTTL,
    }

    [RespCommand]
    private static partial RespOperation<RedisValue[]> HGetEx(
        this in HashCommands context,
        RedisKey key,
        [RespIgnore(HashExpiryMode.None)] HashExpiryMode mode,
        [RespIgnore(-1)] long value,
        [RespPrefix("FIELDS"), RespPrefix] RedisValue[] fields);

    [RespCommand]
    private static partial RespOperation<RedisValue> HGetEx(
        this in HashCommands context,
        RedisKey key,
        [RespIgnore(HashExpiryMode.None)] HashExpiryMode mode,
        [RespIgnore(-1)] long value,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(nameof(HGetEx))]
    private static partial RespOperation<Lease<byte>?> HGetExLease(
        this in HashCommands context,
        RedisKey key,
        [RespIgnore(HashExpiryMode.None)] HashExpiryMode mode,
        [RespIgnore(-1)] long value,
        [RespPrefix("FIELDS"), RespPrefix("1")] RedisValue field);

    [RespCommand(nameof(HGet))]
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

    public static RespOperation<bool> HSetEx(
        this in HashCommands context,
        RedisKey key,
        TimeSpan expiry,
        RedisValue field,
        RedisValue value,
        When when = When.Always)
    {
        var millis = (long)expiry.TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return HSetEx(context, key, when, HashExpiryMode.EX, millis / 1000, field, value);
        }

        return HSetEx(context, key, when, HashExpiryMode.PX, millis, field, value);
    }

    // "Legacy" - OK, so: historically, HashFieldSetAndSetExpiry returned RedisValue; this is ... bizarre,
    // since HSETEX returns a bool. So: in the name of not breaking the world, we'll keep returning RedisValue;
    // but: in the nice clean shiny API: expose bool
    internal static RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        TimeSpan expiry,
        RedisValue field,
        RedisValue value,
        When when)
    {
        var millis = (long)expiry.TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return HSetExLegacy(context, key, when, HashExpiryMode.EX, millis / 1000, field, value);
        }

        return HSetExLegacy(context, key, when, HashExpiryMode.PX, millis, field, value);
    }

    internal static RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        TimeSpan? expiry,
        RedisValue field,
        RedisValue value,
        When when,
        bool keepTtl)
    {
        if (expiry.HasValue) return HSetExLegacy(context, key, expiry.GetValueOrDefault(), field, value, when);
        return HSetExLegacy(context, key, field, value, when, keepTtl);
    }

    public static RespOperation<bool> HSetEx(
        this in HashCommands context,
        RedisKey key,
        TimeSpan expiry,
        HashEntry[] fields,
        When when = When.Always)
    {
        if (fields.Length == 1) return HSetEx(context, key, expiry, fields[0].Name, fields[0].Value, when);
        var millis = (long)expiry.TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return HSetEx(context, key, when, HashExpiryMode.EX, millis / 1000, fields);
        }

        return HSetEx(context, key, when, HashExpiryMode.PX, millis, fields);
    }

    private static RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        TimeSpan expiry,
        HashEntry[] fields,
        When when)
    {
        if (fields.Length == 1) return HSetExLegacy(context, key, expiry, fields[0].Name, fields[0].Value, when);
        var millis = (long)expiry.TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return HSetExLegacy(context, key, when, HashExpiryMode.EX, millis / 1000, fields);
        }

        return HSetExLegacy(context, key, when, HashExpiryMode.PX, millis, fields);
    }

    internal static RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        TimeSpan? expiry,
        HashEntry[] fields,
        When when,
        bool keepTtl)
    {
        if (expiry.HasValue) return HSetExLegacy(context, key, expiry.GetValueOrDefault(), fields, when);
        return HSetExLegacy(context, key, fields, when, keepTtl);
    }

    public static RespOperation<bool> HSetEx(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        RedisValue field,
        RedisValue value,
        When when = When.Always)
    {
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HSetEx(context, key, when, HashExpiryMode.EXAT, millis / 1000, field, value);
        }

        return HSetEx(context, key, when, HashExpiryMode.PXAT, millis, field, value);
    }

    internal static RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        RedisValue field,
        RedisValue value,
        When when)
    {
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HSetExLegacy(context, key, when, HashExpiryMode.EXAT, millis / 1000, field, value);
        }

        return HSetExLegacy(context, key, when, HashExpiryMode.PXAT, millis, field, value);
    }

    public static RespOperation<bool> HSetEx(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        HashEntry[] fields,
        When when = When.Always)
    {
        if (fields.Length == 1) return HSetEx(context, key, expiry, fields[0].Name, fields[0].Value, when);
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HSetEx(context, key, when, HashExpiryMode.EXAT, millis / 1000, fields);
        }

        return HSetEx(context, key, when, HashExpiryMode.PXAT, millis, fields);
    }

    internal static RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        DateTime expiry,
        HashEntry[] fields,
        When when)
    {
        if (fields.Length == 1) return HSetExLegacy(context, key, expiry, fields[0].Name, fields[0].Value, when);
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry);
        if (millis % 1000 == 0) // use seconds
        {
            return HSetExLegacy(context, key, when, HashExpiryMode.EXAT, millis / 1000, fields);
        }

        return HSetExLegacy(context, key, when, HashExpiryMode.PXAT, millis, fields);
    }

    public static RespOperation<bool> HSetEx(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        RedisValue value,
        When when = When.Always,
        bool keepTtl = false)
        => HSetEx(context, key, when, keepTtl ? HashExpiryMode.KEEPTTL : HashExpiryMode.None, -1, field, value);

    private static RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        RedisValue field,
        RedisValue value,
        When when = When.Always,
        bool keepTtl = false)
        => HSetExLegacy(context, key, when, keepTtl ? HashExpiryMode.KEEPTTL : HashExpiryMode.None, -1, field, value);

    public static RespOperation<bool> HSetEx(
        this in HashCommands context,
        RedisKey key,
        HashEntry[] fields,
        When when = When.Always,
        bool keepTtl = false)
    {
        if (fields.Length == 1) return HSetEx(context, key, fields[0].Name, fields[0].Value, when, keepTtl);
        return HSetEx(context, key, when, keepTtl ? HashExpiryMode.KEEPTTL : HashExpiryMode.None, -1, fields);
    }

    private static RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        HashEntry[] fields,
        When when,
        bool keepTtl)
    {
        if (fields.Length == 1) return HSetExLegacy(context, key, fields[0].Name, fields[0].Value, when, keepTtl);
        return HSetExLegacy(context, key, when, keepTtl ? HashExpiryMode.KEEPTTL : HashExpiryMode.None, -1, fields);
    }

    [RespCommand(Formatter = "HSetExFormatter.Instance")]
    private static partial RespOperation<bool> HSetEx(
        this in HashCommands context,
        RedisKey key,
        When when,
        HashExpiryMode mode,
        long expiry,
        RedisValue field,
        RedisValue value);

    [RespCommand(Formatter = "HSetExFormatter.Instance")]
    private static partial RespOperation<bool> HSetEx(
        this in HashCommands context,
        RedisKey key,
        When when,
        HashExpiryMode mode,
        long expiry,
        HashEntry[] fields);

    [RespCommand(nameof(HSetEx), Formatter = "HSetExFormatter.Instance")]
    private static partial RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        When when,
        HashExpiryMode mode,
        long expiry,
        RedisValue field,
        RedisValue value);

    [RespCommand(nameof(HSetEx), Formatter = "HSetExFormatter.Instance")]
    private static partial RespOperation<RedisValue> HSetExLegacy(
        this in HashCommands context,
        RedisKey key,
        When when,
        HashExpiryMode mode,
        long expiry,
        HashEntry[] fields);

    private sealed class
        HSetExFormatter : IRespFormatter<(RedisKey Key, When When, HashExpiryMode Mode, long Expiry, HashEntry[] Fields)>,
            IRespFormatter<(RedisKey Key, When When, HashExpiryMode Mode, long Expiry, RedisValue Field, RedisValue Value)>
    {
        private HSetExFormatter() { }
        public static readonly HSetExFormatter Instance = new();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, When When, HashExpiryMode Mode, long Expiry, HashEntry[] Fields) request)
        {
            bool __inc0 = request.When != When.Always; // IgnoreExpression
            bool __inc1 = request.Mode != HashExpiryMode.None; // IgnoreExpression
            bool __inc2 = request.Expiry != -1; // IgnoreExpression
#pragma warning disable SA1118
            writer.WriteCommand(command, 3 // constant args: key, FIELDS, numfields
                                         + (__inc0 ? 1 : 0) // request.When
                                         + (__inc1 ? 1 : 0) // request.Mode
                                         + (__inc2 ? 1 : 0) // request.Expiry
                                         + (request.Fields.Length * 2)); // request.Fields
#pragma warning restore SA1118
            writer.Write(request.Key);
            if (__inc0)
            {
                writer.WriteRaw(GetRaw(request.When));
            }
            if (__inc1)
            {
                writer.WriteBulkString(request.Mode);
            }
            if (__inc2)
            {
                writer.WriteBulkString(request.Expiry);
            }
            writer.WriteRaw("$6\r\nFIELDS\r\n"u8); // FIELDS
            writer.WriteBulkString(request.Fields.Length);
            foreach (var entry in request.Fields)
            {
                writer.Write(entry.Name);
                writer.Write(entry.Value);
            }
        }

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, When When, HashExpiryMode Mode, long Expiry, RedisValue Field, RedisValue Value) request)
        {
            bool __inc0 = request.When != When.Always; // IgnoreExpression
            bool __inc1 = request.Mode != HashExpiryMode.None; // IgnoreExpression
            bool __inc2 = request.Expiry != -1; // IgnoreExpression
#pragma warning disable SA1118
            writer.WriteCommand(command, 5 // constant args: key, FIELDS, numfields, field, value
                                         + (__inc0 ? 1 : 0) // request.When
                                         + (__inc1 ? 1 : 0) // request.Mode
                                         + (__inc2 ? 1 : 0)); // request.Expiry
#pragma warning restore SA1118
            writer.Write(request.Key);
            if (__inc0)
            {
                writer.WriteRaw(GetRaw(request.When));
            }
            if (__inc1)
            {
                writer.WriteBulkString(request.Mode);
            }
            if (__inc2)
            {
                writer.WriteBulkString(request.Expiry);
            }
            writer.WriteRaw("$6\r\nFIELDS\r\n$1\r\n1\r\n"u8); // FIELDS 1
            writer.Write(request.Field);
            writer.Write(request.Value);
        }

        private static ReadOnlySpan<byte> GetRaw(When when)
        {
            return when switch
            {
                When.Exists => "FXX"u8,
                When.NotExists => "FNX"u8,
                _ => Throw(),
            };
            static ReadOnlySpan<byte> Throw() => throw new ArgumentOutOfRangeException(nameof(when));
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
