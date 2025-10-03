using System.Runtime.CompilerServices;
using RESPite.Messages;
using StackExchange.Redis;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming
namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly KeyCommands Keys(this in RespContext context)
        => ref Unsafe.As<RespContext, KeyCommands>(ref Unsafe.AsRef(in context));
}

public readonly struct KeyCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class KeyCommandsExtensions
{
    [RespCommand(Formatter = "CopyFormatter.Instance")]
    public static partial RespOperation<bool> Copy(
        this in KeyCommands context,
        RedisKey source,
        RedisKey destination,
        int destinationDatabase = -1,
        bool replace = false);

    [RespCommand]
    public static partial RespOperation<bool> Del(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> Del(this in KeyCommands context, [RespKey] RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<byte[]?> Dump(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<bool> Exists(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> Exists(this in KeyCommands context, [RespKey] RedisKey[] keys);

    public static RespOperation<bool> Expire(
        this in KeyCommands context,
        RedisKey key,
        TimeSpan? expiry,
        ExpireWhen when = ExpireWhen.Always)
    {
        if (expiry is null || expiry == TimeSpan.MaxValue)
        {
            if (when != ExpireWhen.Always) Throw(when);
            return Persist(context, key);
            static void Throw(ExpireWhen when) => throw new ArgumentException($"PERSIST cannot be used with {when}.");
        }

        var millis = (long)expiry.GetValueOrDefault().TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return Expire(context, key, millis / 1000, when);
        }

        return PExpire(context, key, millis, when);
    }

    [RespCommand]
    public static partial RespOperation<bool> Expire(
        this in KeyCommands context,
        RedisKey key,
        long seconds,
        [RespIgnore(ExpireWhen.Always)] ExpireWhen when = ExpireWhen.Always);

    public static RespOperation<bool> ExpireAt(
        this in KeyCommands context,
        RedisKey key,
        DateTime? expiry,
        ExpireWhen when = ExpireWhen.Always)
    {
        if (expiry is null || expiry == DateTime.MaxValue)
        {
            if (when != ExpireWhen.Always) Throw(when);
            return Persist(context, key);
            static void Throw(ExpireWhen when) => throw new ArgumentException($"PERSIST cannot be used with {when}.");
        }

        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry.GetValueOrDefault());
        if (millis % 1000 == 0) // use seconds
        {
            return ExpireAt(context, key, millis / 1000, when);
        }

        return PExpireAt(context, key, millis, when);
    }

    [RespCommand]
    public static partial RespOperation<bool> ExpireAt(
        this in KeyCommands context,
        RedisKey key,
        long seconds,
        [RespIgnore(ExpireWhen.Always)] ExpireWhen when = ExpireWhen.Always);

    [RespCommand(Parser = "RespParsers.DateTimeFromSeconds")]
    public static partial RespOperation<DateTime?> ExpireTime(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<bool> Move(this in KeyCommands context, RedisKey key, int db);

    [RespCommand("object")]
    public static partial RespOperation<string?> ObjectEncoding(
        this in KeyCommands context,
        [RespPrefix("ENCODING")] RedisKey key);

    [RespCommand("object")]
    public static partial RespOperation<long?> ObjectFreq(
        this in KeyCommands context,
        [RespPrefix("FREQ")] RedisKey key);

    [RespCommand("object", Parser = "RespParsers.TimeSpanFromSeconds")]
    public static partial RespOperation<TimeSpan?> ObjectIdleTime(
        this in KeyCommands context,
        [RespPrefix("IDLETIME")] RedisKey key);

    [RespCommand("object")]
    public static partial RespOperation<long?> ObjectRefCount(
        this in KeyCommands context,
        [RespPrefix("REFCOUNT")] RedisKey key);

    [RespCommand]
    public static partial RespOperation<bool> PExpire(
        this in KeyCommands context,
        RedisKey key,
        long milliseconds,
        [RespIgnore(ExpireWhen.Always)] ExpireWhen when = ExpireWhen.Always);

    [RespCommand]
    public static partial RespOperation<bool> PExpireAt(
        this in KeyCommands context,
        RedisKey key,
        long milliseconds,
        [RespIgnore(ExpireWhen.Always)] ExpireWhen when = ExpireWhen.Always);

    [RespCommand(Parser = "RespParsers.DateTimeFromMilliseconds")]
    public static partial RespOperation<DateTime?> PExpireTime(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<bool> Persist(this in KeyCommands context, RedisKey key);

    [RespCommand(Parser = "RespParsers.TimeSpanFromMilliseconds")]
    public static partial RespOperation<TimeSpan?> Pttl(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisKey> RandomKey(this in KeyCommands context);

    [RespCommand]
    public static partial RespOperation<bool> Rename(this in KeyCommands context, RedisKey key, RedisKey newKey);

    [RespCommand]
    public static RespOperation<bool> Rename(this in KeyCommands context, RedisKey key, RedisKey newKey, When when)
    {
        switch (when)
        {
            case When.Always:
                return Rename(context, key, newKey);
            case When.NotExists:
                return RenameNx(context, key, newKey);
            default:
                when.AlwaysOrNotExists(); // throws
                return default;
        }
    }

    [RespCommand]
    public static partial RespOperation<bool> RenameNx(this in KeyCommands context, RedisKey key, RedisKey newKey);

    [RespCommand(Formatter = "RestoreFormatter.Instance")]
    public static partial RespOperation Restore(
        this in KeyCommands context,
        RedisKey key,
        TimeSpan? ttl,
        byte[] serializedValue);

    [RespCommand]
    public static partial RespOperation<bool> Touch(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> Touch(this in KeyCommands context, [RespKey] RedisKey[] keys);

    [RespCommand(Parser = "RespParsers.TimeSpanFromSeconds")]
    public static partial RespOperation<TimeSpan?> Ttl(this in KeyCommands context, RedisKey key);

    [RespCommand(Parser = "RedisTypeParser.Instance")]
    public static partial RespOperation<RedisType> Type(this in KeyCommands context, RedisKey key);

    private sealed class RedisTypeParser : IRespParser<RedisType>
    {
        public static readonly RedisTypeParser Instance = new();
        private RedisTypeParser() { }

        public RedisType Parse(ref RespReader reader)
        {
            if (reader.IsNull) return RedisType.None;
            if (reader.Is("zset"u8)) return RedisType.SortedSet;
            return reader.ReadEnum(RedisType.Unknown);
        }
    }

    private sealed class CopyFormatter : IRespFormatter<(RedisKey Source, RedisKey Destination, int DestinationDatabase,
        bool Replace)>
    {
        public static readonly CopyFormatter Instance = new();
        private CopyFormatter() { }

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Source, RedisKey Destination, int DestinationDatabase, bool Replace) request)
        {
            writer.WriteCommand(command, (request.DestinationDatabase >= 0 ? 4 : 2) + (request.Replace ? 1 : 0));
            writer.Write(request.Source);
            writer.Write(request.Destination);
            if (request.DestinationDatabase >= 0)
            {
                writer.WriteRaw("$2\r\nDB\r\n"u8);
                writer.WriteBulkString(request.DestinationDatabase);
            }

            if (request.Replace)
            {
                writer.WriteRaw("$7\r\nREPLACE\r\n"u8);
            }
        }
    }

    private sealed class RestoreFormatter : IRespFormatter<(RedisKey Key, TimeSpan? Ttl, byte[] SerializedValue)>
    {
        public static readonly RestoreFormatter Instance = new();
        private RestoreFormatter() { }

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, TimeSpan? Ttl, byte[] SerializedValue) request)
        {
            writer.WriteCommand(command, 3);
            writer.Write(request.Key);
            if (request.Ttl.HasValue)
            {
                writer.WriteBulkString((long)request.Ttl.Value.TotalMilliseconds);
            }
            else
            {
                writer.WriteRaw("$1\r\n0\r\n"u8);
            }

            writer.WriteBulkString(request.SerializedValue);
        }
    }
}
